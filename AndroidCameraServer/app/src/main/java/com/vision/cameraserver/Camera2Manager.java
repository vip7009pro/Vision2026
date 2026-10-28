package com.vision.cameraserver;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.graphics.SurfaceTexture;
import android.graphics.YuvImage;
import android.hardware.camera2.*;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.util.Range;
import android.util.Rational;
import android.util.Size;
import android.view.Surface;
import android.view.TextureView;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

public class Camera2Manager {
    private static final String TAG = "Camera2Manager";

    public interface ParameterChangeListener {
        void onParametersChanged(String lensName, float ev, long exposureTimeNs, boolean autoExposure, String focusMode, float focusDistance, String iso, String whiteBalance, boolean torch);
    }

    private final Context mContext;
    private final CameraManager mCameraManager;
    private final TextureView mTextureView;
    private ParameterChangeListener mListener;

    private HandlerThread mBackgroundThread;
    private Handler mBackgroundHandler;

    private CameraDevice mCameraDevice;
    private CameraCaptureSession mCaptureSession;
    private CaptureRequest.Builder mPreviewRequestBuilder;
    private CaptureRequest mPreviewRequest;

    private ImageReader mPreviewImageReader;
    private ImageReader mStillImageReader;

    private volatile byte[] mLatestPreviewJpeg = null;
    private final Object mFrameLock = new Object();

    // Galaxy S24 Ultra Multi-Camera Lens IDs mapping
    private int mCurrentLensIndex = 0; // 0: Primary 1x, 1: Tele 3x, 2: Tele 5x, 3: UltraWide 0.6x, 4: Front
    private String mCurrentCameraId = "0";

    // Pro Mode Parameters
    private float mEv = 0.0f;
    private boolean mAutoExposure = true;
    private long mExposureTimeNs = 10_000_000L; // 10ms = 1/100s default
    private String mFocusMode = "continuous-picture";
    private float mFocusDistanceRatio = 0.0f; // 0.0 (vô cực) -> 1.0 (cận cảnh)
    private String mIso = "auto";
    private String mWhiteBalance = "auto";
    private boolean mTorchEnabled = false;

    private float mMinFocusDistance = 10.0f;
    private Range<Integer> mExposureCompensationRange;
    private Rational mExposureCompensationStep = new Rational(1, 3);
    private Range<Long> mExposureTimeRange;
    private Range<Integer> mIsoRange;

    public Camera2Manager(Context context, TextureView textureView) {
        mContext = context;
        mTextureView = textureView;
        mCameraManager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
    }

    public void setParameterChangeListener(ParameterChangeListener listener) {
        mListener = listener;
    }

    public void start() {
        startBackgroundThread();
        if (mTextureView.isAvailable()) {
            openCamera(mCurrentLensIndex);
        } else {
            mTextureView.setSurfaceTextureListener(new TextureView.SurfaceTextureListener() {
                @Override
                public void onSurfaceTextureAvailable(SurfaceTexture surface, int width, int height) {
                    openCamera(mCurrentLensIndex);
                }

                @Override
                public void onSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) {}

                @Override
                public boolean onSurfaceTextureDestroyed(SurfaceTexture surface) {
                    return true;
                }

                @Override
                public void onSurfaceTextureUpdated(SurfaceTexture surface) {}
            });
        }
    }

    public void stop() {
        closeCamera();
        stopBackgroundThread();
    }

    private void startBackgroundThread() {
        if (mBackgroundThread == null) {
            mBackgroundThread = new HandlerThread("CameraBackground");
            mBackgroundThread.start();
            mBackgroundHandler = new Handler(mBackgroundThread.getLooper());
        }
    }

    private void stopBackgroundThread() {
        if (mBackgroundThread != null) {
            mBackgroundThread.quitSafely();
            try {
                mBackgroundThread.join();
                mBackgroundThread = null;
                mBackgroundHandler = null;
            } catch (InterruptedException e) {
                Log.e(TAG, "Error stopping background thread", e);
            }
        }
    }

    @SuppressLint("MissingPermission")
    public synchronized void openCamera(int lensIndex) {
        closeCamera();
        mCurrentLensIndex = lensIndex;
        mCurrentCameraId = resolveCameraIdForLens(lensIndex);

        try {
            CameraCharacteristics characteristics = mCameraManager.getCameraCharacteristics(mCurrentCameraId);
            readCameraCapabilities(characteristics);

            StreamConfigurationMap map = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
            if (map == null) return;

            // Kích thước Preview tối ưu (1920x1080 hoặc lớn nhất)
            Size previewSize = chooseOptimalSize(map.getOutputSizes(SurfaceTexture.class), 1920, 1080);
            Size stillSize = Collections.max(Arrays.asList(map.getOutputSizes(ImageFormat.JPEG)), new CompareSizesByArea());

            // ImageReader cho luồng video stream / preview JPEG
            mPreviewImageReader = ImageReader.newInstance(previewSize.getWidth(), previewSize.getHeight(), ImageFormat.YUV_420_888, 2);
            mPreviewImageReader.setOnImageAvailableListener(mOnPreviewImageAvailableListener, mBackgroundHandler);

            // ImageReader cho ảnh tĩnh cảm biến gốc độ nét cao (Full-Res Snapshot)
            mStillImageReader = ImageReader.newInstance(stillSize.getWidth(), stillSize.getHeight(), ImageFormat.JPEG, 2);

            mCameraManager.openCamera(mCurrentCameraId, mStateCallback, mBackgroundHandler);
        } catch (Exception e) {
            Log.e(TAG, "Cannot open camera: " + mCurrentCameraId, e);
        }
    }

    private String resolveCameraIdForLens(int lensIndex) {
        try {
            String[] ids = mCameraManager.getCameraIdList();
            // Galaxy S24 Ultra: "0" = Wide 1x 200MP, "2" = UltraWide 0.6x, "3" = Tele 3x, "4" = Tele 5x, "1" = Front
            switch (lensIndex) {
                case 0: // PrimaryWide1x (200MP)
                    return containsId(ids, "0") ? "0" : ids[0];
                case 1: // Tele3x
                    return containsId(ids, "3") ? "3" : (containsId(ids, "0") ? "0" : ids[0]);
                case 2: // Tele5xPeriscope (50MP)
                    return containsId(ids, "4") ? "4" : (containsId(ids, "3") ? "3" : ids[0]);
                case 3: // UltraWide06x (Macro)
                    return containsId(ids, "2") ? "2" : (containsId(ids, "0") ? "0" : ids[0]);
                case 4: // FrontSelfie
                    return containsId(ids, "1") ? "1" : (ids.length > 1 ? ids[1] : ids[0]);
                default:
                    return ids[0];
            }
        } catch (Exception e) {
            return "0";
        }
    }

    private boolean containsId(String[] ids, String target) {
        for (String id : ids) {
            if (id.equals(target)) return true;
        }
        return false;
    }

    private void readCameraCapabilities(CameraCharacteristics characteristics) {
        mExposureCompensationRange = characteristics.get(CameraCharacteristics.CONTROL_AE_COMPENSATION_RANGE);
        Rational step = characteristics.get(CameraCharacteristics.CONTROL_AE_COMPENSATION_STEP);
        if (step != null) mExposureCompensationStep = step;

        mExposureTimeRange = characteristics.get(CameraCharacteristics.SENSOR_INFO_EXPOSURE_TIME_RANGE);
        mIsoRange = characteristics.get(CameraCharacteristics.SENSOR_INFO_SENSITIVITY_RANGE);

        Float minFocus = characteristics.get(CameraCharacteristics.LENS_INFO_MINIMUM_FOCUS_DISTANCE);
        mMinFocusDistance = minFocus != null ? minFocus : 10.0f;
    }

    private final CameraDevice.StateCallback mStateCallback = new CameraDevice.StateCallback() {
        @Override
        public void onOpened(CameraDevice camera) {
            mCameraDevice = camera;
            createCameraPreviewSession();
        }

        @Override
        public void onDisconnected(CameraDevice camera) {
            camera.close();
            mCameraDevice = null;
        }

        @Override
        public void onError(CameraDevice camera, int error) {
            camera.close();
            mCameraDevice = null;
        }
    };

    private void createCameraPreviewSession() {
        try {
            SurfaceTexture texture = mTextureView.getSurfaceTexture();
            if (texture == null) return;

            texture.setDefaultBufferSize(1920, 1080);
            Surface previewSurface = new Surface(texture);
            Surface streamSurface = mPreviewImageReader.getSurface();
            Surface stillSurface = mStillImageReader.getSurface();

            mPreviewRequestBuilder = mCameraDevice.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
            mPreviewRequestBuilder.addTarget(previewSurface);
            mPreviewRequestBuilder.addTarget(streamSurface);

            mCameraDevice.createCaptureSession(Arrays.asList(previewSurface, streamSurface, stillSurface),
                    new CameraCaptureSession.StateCallback() {
                        @Override
                        public void onConfigured(CameraCaptureSession session) {
                            if (mCameraDevice == null) return;
                            mCaptureSession = session;
                            applyParametersToPreview();
                        }

                        @Override
                        public void onConfigureFailed(CameraCaptureSession session) {
                            Log.e(TAG, "Configuration failed for camera session");
                        }
                    }, mBackgroundHandler);
        } catch (Exception e) {
            Log.e(TAG, "Error creating camera preview session", e);
        }
    }

    public synchronized void applyParametersToPreview() {
        if (mCaptureSession == null || mPreviewRequestBuilder == null) return;

        try {
            // 1. Phơi sáng & Bù trừ sáng EV
            if (mExposureCompensationRange != null && mExposureCompensationStep != null) {
                float stepVal = mExposureCompensationStep.floatValue();
                int index = Math.round(mEv / stepVal);
                index = Math.max(mExposureCompensationRange.getLower(), Math.min(index, mExposureCompensationRange.getUpper()));
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AE_EXPOSURE_COMPENSATION, index);
            }

            // 2. Thời gian phơi sáng Shutter Speed
            if (mAutoExposure) {
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_ON);
            } else {
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_OFF);
                long exp = mExposureTimeNs;
                if (mExposureTimeRange != null) {
                    exp = Math.max(mExposureTimeRange.getLower(), Math.min(exp, mExposureTimeRange.getUpper()));
                }
                mPreviewRequestBuilder.set(CaptureRequest.SENSOR_EXPOSURE_TIME, exp);
            }

            // 3. Độ nhạy sáng ISO
            if (!mIso.equalsIgnoreCase("auto")) {
                try {
                    int isoVal = Integer.parseInt(mIso.trim());
                    if (mIsoRange != null) {
                        isoVal = Math.max(mIsoRange.getLower(), Math.min(isoVal, mIsoRange.getUpper()));
                    }
                    mPreviewRequestBuilder.set(CaptureRequest.SENSOR_SENSITIVITY, isoVal);
                } catch (Exception ignored) {}
            }

            // 4. Lấy nét Focus (Auto / Macro / Manual)
            if ("macro".equalsIgnoreCase(mFocusMode)) {
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_MACRO);
            } else if ("manual".equalsIgnoreCase(mFocusMode)) {
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_OFF);
                float diopters = mFocusDistanceRatio * mMinFocusDistance;
                mPreviewRequestBuilder.set(CaptureRequest.LENS_FOCUS_DISTANCE, diopters);
            } else {
                mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE);
            }

            // 5. Cân bằng trắng (White Balance)
            int awbMode = CaptureRequest.CONTROL_AWB_MODE_AUTO;
            if ("daylight".equalsIgnoreCase(mWhiteBalance)) awbMode = CaptureRequest.CONTROL_AWB_MODE_DAYLIGHT;
            else if ("cloudy".equalsIgnoreCase(mWhiteBalance)) awbMode = CaptureRequest.CONTROL_AWB_MODE_CLOUDY_DAYLIGHT;
            else if ("incandescent".equalsIgnoreCase(mWhiteBalance)) awbMode = CaptureRequest.CONTROL_AWB_MODE_INCANDESCENT;
            else if ("fluorescent".equalsIgnoreCase(mWhiteBalance)) awbMode = CaptureRequest.CONTROL_AWB_MODE_FLUORESCENT;
            mPreviewRequestBuilder.set(CaptureRequest.CONTROL_AWB_MODE, awbMode);

            // 6. Đèn Flash LED Torch
            if (mTorchEnabled) {
                mPreviewRequestBuilder.set(CaptureRequest.FLASH_MODE, CaptureRequest.FLASH_MODE_TORCH);
            } else {
                mPreviewRequestBuilder.set(CaptureRequest.FLASH_MODE, CaptureRequest.FLASH_MODE_OFF);
            }

            mPreviewRequest = mPreviewRequestBuilder.build();
            mCaptureSession.setRepeatingRequest(mPreviewRequest, null, mBackgroundHandler);

            notifyParametersChanged();
        } catch (Exception e) {
            Log.e(TAG, "Error applying camera parameters", e);
        }
    }

    private void notifyParametersChanged() {
        if (mListener != null) {
            mListener.onParametersChanged(getLensDisplayName(mCurrentLensIndex), mEv, mExposureTimeNs, mAutoExposure, mFocusMode, mFocusDistanceRatio, mIso, mWhiteBalance, mTorchEnabled);
        }
    }

    public String getLensDisplayName(int index) {
        switch (index) {
            case 0: return "1x Primary Wide (200MP)";
            case 1: return "3x Telephoto (10MP)";
            case 2: return "5x Periscope Tele (50MP)";
            case 3: return "0.6x Ultra-Wide Macro (12MP)";
            case 4: return "Front Selfie (12MP)";
            default: return "Camera #" + index;
        }
    }

    public synchronized void setLens(int lensIndex) {
        if (mCurrentLensIndex != lensIndex) {
            openCamera(lensIndex);
        }
    }

    public synchronized void setEv(float ev) {
        mEv = ev;
        applyParametersToPreview();
    }

    public synchronized void setAutoExposure(boolean auto) {
        mAutoExposure = auto;
        applyParametersToPreview();
    }

    public synchronized void setExposureTimeNs(long ns) {
        mAutoExposure = false;
        mExposureTimeNs = ns;
        applyParametersToPreview();
    }

    public synchronized void setFocusMode(String mode) {
        mFocusMode = mode;
        applyParametersToPreview();
    }

    public synchronized void setFocusDistanceRatio(float ratio) {
        mFocusMode = "manual";
        mFocusDistanceRatio = Math.max(0.0f, Math.min(ratio, 1.0f));
        applyParametersToPreview();
    }

    public synchronized void setIso(String iso) {
        mIso = iso;
        applyParametersToPreview();
    }

    public synchronized void setWhiteBalance(String wb) {
        mWhiteBalance = wb;
        applyParametersToPreview();
    }

    public synchronized void setTorch(boolean enabled) {
        mTorchEnabled = enabled;
        applyParametersToPreview();
    }

    public synchronized void toggleTorch() {
        setTorch(!mTorchEnabled);
    }

    public synchronized void switchNextLens() {
        int next = (mCurrentLensIndex + 1) % 4; // lặp 4 camera sau
        setLens(next);
    }

    public byte[] getLatestPreviewJpeg() {
        return mLatestPreviewJpeg;
    }

    public byte[] takeFullResSnapshot(int timeoutMs) {
        if (mCameraDevice == null || mCaptureSession == null || mStillImageReader == null) {
            return mLatestPreviewJpeg;
        }

        final byte[][] result = new byte[1][];
        final CountDownLatch latch = new CountDownLatch(1);

        mStillImageReader.setOnImageAvailableListener(reader -> {
            Image image = null;
            try {
                image = reader.acquireLatestImage();
                if (image != null) {
                    ByteBuffer buffer = image.getPlanes()[0].getBuffer();
                    byte[] bytes = new byte[buffer.remaining()];
                    buffer.get(bytes);
                    result[0] = bytes;
                }
            } catch (Exception e) {
                Log.e(TAG, "Error acquiring still capture", e);
            } finally {
                if (image != null) image.close();
                latch.countDown();
            }
        }, mBackgroundHandler);

        try {
            CaptureRequest.Builder captureBuilder = mCameraDevice.createCaptureRequest(CameraDevice.TEMPLATE_STILL_CAPTURE);
            captureBuilder.addTarget(mStillImageReader.getSurface());

            // Kế thừa các thông số Pro Mode hiện tại
            captureBuilder.set(CaptureRequest.CONTROL_AF_MODE, mPreviewRequestBuilder.get(CaptureRequest.CONTROL_AF_MODE));
            if ("manual".equalsIgnoreCase(mFocusMode)) {
                captureBuilder.set(CaptureRequest.LENS_FOCUS_DISTANCE, mPreviewRequestBuilder.get(CaptureRequest.LENS_FOCUS_DISTANCE));
            }
            captureBuilder.set(CaptureRequest.CONTROL_AE_MODE, mPreviewRequestBuilder.get(CaptureRequest.CONTROL_AE_MODE));
            if (!mAutoExposure) {
                captureBuilder.set(CaptureRequest.SENSOR_EXPOSURE_TIME, mPreviewRequestBuilder.get(CaptureRequest.SENSOR_EXPOSURE_TIME));
            }
            if (!mIso.equalsIgnoreCase("auto")) {
                captureBuilder.set(CaptureRequest.SENSOR_SENSITIVITY, mPreviewRequestBuilder.get(CaptureRequest.SENSOR_SENSITIVITY));
            }
            captureBuilder.set(CaptureRequest.CONTROL_AWB_MODE, mPreviewRequestBuilder.get(CaptureRequest.CONTROL_AWB_MODE));
            captureBuilder.set(CaptureRequest.FLASH_MODE, mPreviewRequestBuilder.get(CaptureRequest.FLASH_MODE));

            mCaptureSession.capture(captureBuilder.build(), null, mBackgroundHandler);
            latch.await(timeoutMs, TimeUnit.MILLISECONDS);
        } catch (Exception e) {
            Log.e(TAG, "Error taking still snapshot", e);
        }

        return result[0] != null ? result[0] : mLatestPreviewJpeg;
    }

    private final ImageReader.OnImageAvailableListener mOnPreviewImageAvailableListener = new ImageReader.OnImageAvailableListener() {
        @Override
        public void onImageAvailable(ImageReader reader) {
            Image image = null;
            try {
                image = reader.acquireLatestImage();
                if (image == null) return;

                byte[] jpeg = yuv420ToJpeg(image);
                if (jpeg != null) {
                    synchronized (mFrameLock) {
                        mLatestPreviewJpeg = jpeg;
                        mFrameLock.notifyAll();
                    }
                }
            } catch (Exception ignored) {
            } finally {
                if (image != null) image.close();
            }
        }
    };

    public Object getFrameLock() {
        return mFrameLock;
    }

    private byte[] yuv420ToJpeg(Image image) {
        try {
            int width = image.getWidth();
            int height = image.getHeight();
            byte[] nv21 = yuv420ToNv21(image);

            YuvImage yuvImage = new YuvImage(nv21, ImageFormat.NV21, width, height, null);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            yuvImage.compressToJpeg(new Rect(0, 0, width, height), 85, out);
            return out.toByteArray();
        } catch (Exception e) {
            return null;
        }
    }

    private byte[] yuv420ToNv21(Image image) {
        int width = image.getWidth();
        int height = image.getHeight();
        byte[] nv21 = new byte[width * height * 3 / 2];

        Image.Plane yPlane = image.getPlanes()[0];
        Image.Plane uPlane = image.getPlanes()[1];
        Image.Plane vPlane = image.getPlanes()[2];

        ByteBuffer yBuffer = yPlane.getBuffer();
        ByteBuffer uBuffer = uPlane.getBuffer();
        ByteBuffer vBuffer = vPlane.getBuffer();

        int yRowStride = yPlane.getRowStride();
        int yPixelStride = yPlane.getPixelStride();

        int pos = 0;
        for (int row = 0; row < height; row++) {
            yBuffer.position(row * yRowStride);
            for (int col = 0; col < width; col++) {
                nv21[pos++] = yBuffer.get();
                if (yPixelStride > 1 && col < width - 1) {
                    yBuffer.position(yBuffer.position() + yPixelStride - 1);
                }
            }
        }

        int uvRowStride = uPlane.getRowStride();
        int uvPixelStride = uPlane.getPixelStride();

        int uvHeight = height / 2;
        int uvWidth = width / 2;

        for (int row = 0; row < uvHeight; row++) {
            vBuffer.position(row * uvRowStride);
            uBuffer.position(row * uvRowStride);
            for (int col = 0; col < uvWidth; col++) {
                nv21[pos++] = vBuffer.get();
                nv21[pos++] = uBuffer.get();
                if (uvPixelStride > 1 && col < uvWidth - 1) {
                    vBuffer.position(vBuffer.position() + uvPixelStride - 1);
                    uBuffer.position(uBuffer.position() + uvPixelStride - 1);
                }
            }
        }

        return nv21;
    }

    public synchronized void closeCamera() {
        if (mCaptureSession != null) {
            try { mCaptureSession.close(); } catch (Exception ignored) {}
            mCaptureSession = null;
        }
        if (mCameraDevice != null) {
            try { mCameraDevice.close(); } catch (Exception ignored) {}
            mCameraDevice = null;
        }
        if (mPreviewImageReader != null) {
            try { mPreviewImageReader.close(); } catch (Exception ignored) {}
            mPreviewImageReader = null;
        }
        if (mStillImageReader != null) {
            try { mStillImageReader.close(); } catch (Exception ignored) {}
            mStillImageReader = null;
        }
    }

    private static Size chooseOptimalSize(Size[] choices, int targetWidth, int targetHeight) {
        List<Size> bigEnough = new ArrayList<>();
        for (Size option : choices) {
            if (option.getWidth() >= targetWidth && option.getHeight() >= targetHeight) {
                bigEnough.add(option);
            }
        }
        if (bigEnough.size() > 0) {
            return Collections.min(bigEnough, new CompareSizesByArea());
        } else {
            return choices[0];
        }
    }

    static class CompareSizesByArea implements Comparator<Size> {
        @Override
        public int compare(Size lhs, Size rhs) {
            return Long.signum((long) lhs.getWidth() * lhs.getHeight() - (long) rhs.getWidth() * rhs.getHeight());
        }
    }

    // Getters for status JSON
    public int getCurrentLensIndex() { return mCurrentLensIndex; }
    public float getEv() { return mEv; }
    public boolean isAutoExposure() { return mAutoExposure; }
    public long getExposureTimeNs() { return mExposureTimeNs; }
    public String getFocusMode() { return mFocusMode; }
    public float getFocusDistanceRatio() { return mFocusDistanceRatio; }
    public String getIso() { return mIso; }
    public String getWhiteBalance() { return mWhiteBalance; }
    public boolean isTorchEnabled() { return mTorchEnabled; }
}
