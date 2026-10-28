package com.vision.cameraserver;

import android.Manifest;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.os.PowerManager;
import android.view.TextureView;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

public class MainActivity extends AppCompatActivity implements Camera2Manager.ParameterChangeListener {

    private static final int PERMISSION_REQUEST_CODE = 1001;

    private TextureView mTextureView;
    private TextView mTvServerBadge;
    private TextView mTvIpUsb;
    private TextView mTvIpWifi;
    private TextView mTvHudLens;
    private TextView mTvHudExposure;
    private TextView mTvHudEv;
    private TextView mTvHudFocus;
    private TextView mTvHudIsoWb;
    private Button mBtnToggleServer;
    private Button mBtnSwitchLens;
    private Button mBtnToggleTorch;

    private Camera2Manager mCameraManager;
    private HttpCameraServer mHttpServer;
    private PowerManager.WakeLock mWakeLock;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Giữ màn hình luôn sáng phục vụ dây chuyền sản xuất 24/7
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        setContentView(R.layout.activity_main);

        bindViews();
        acquireWakeLock();

        mCameraManager = new Camera2Manager(this, mTextureView);
        mCameraManager.setParameterChangeListener(this);

        mHttpServer = new HttpCameraServer(8080, mCameraManager);

        setupButtons();
        updateNetworkInfo();

        if (checkPermissions()) {
            startCameraAndServer();
        } else {
            requestPermissions();
        }
    }

    private void bindViews() {
        mTextureView = findViewById(R.id.cameraTextureView);
        mTvServerBadge = findViewById(R.id.tvServerBadge);
        mTvIpUsb = findViewById(R.id.tvIpUsb);
        mTvIpWifi = findViewById(R.id.tvIpWifi);
        mTvHudLens = findViewById(R.id.tvHudLens);
        mTvHudExposure = findViewById(R.id.tvHudExposure);
        mTvHudEv = findViewById(R.id.tvHudEv);
        mTvHudFocus = findViewById(R.id.tvHudFocus);
        mTvHudIsoWb = findViewById(R.id.tvHudIsoWb);
        mBtnToggleServer = findViewById(R.id.btnToggleServer);
        mBtnSwitchLens = findViewById(R.id.btnSwitchLens);
        mBtnToggleTorch = findViewById(R.id.btnToggleTorch);
    }

    private void setupButtons() {
        mBtnToggleServer.setOnClickListener(v -> {
            if (mHttpServer != null && mHttpServer.isRunning()) {
                mHttpServer.stop();
                mTvServerBadge.setText("SERVER DỪNG");
                mTvServerBadge.setBackgroundColor(0xFFEF4444); // red
                mBtnToggleServer.setText("BẬT SERVER");
                mBtnToggleServer.setBackgroundTintList(android.content.res.ColorStateList.valueOf(0xFF10B981)); // green
            } else {
                try {
                    if (mHttpServer != null) mHttpServer.start();
                    mTvServerBadge.setText("ONLINE :8080");
                    mTvServerBadge.setBackgroundColor(0xFF2E7D32); // green
                    mBtnToggleServer.setText("DỪNG SERVER");
                    mBtnToggleServer.setBackgroundTintList(android.content.res.ColorStateList.valueOf(0xFFEF4444)); // red
                } catch (Exception e) {
                    Toast.makeText(this, "Lỗi khởi động Server: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                }
            }
        });

        mBtnSwitchLens.setOnClickListener(v -> {
            if (mCameraManager != null) {
                mCameraManager.switchNextLens();
            }
        });

        mBtnToggleTorch.setOnClickListener(v -> {
            if (mCameraManager != null) {
                mCameraManager.toggleTorch();
            }
        });
    }

    private void updateNetworkInfo() {
        String wifiIp = NetworkUtils.getWifiIpAddress(this);
        mTvIpWifi.setText("🌐 Wi-Fi / LAN IP: http://" + wifiIp + ":8080");
        mTvIpUsb.setText("⚡ Cáp USB (ADB): http://127.0.0.1:8080 (Độ trễ < 5ms)");
    }

    private void startCameraAndServer() {
        mCameraManager.start();
        try {
            mHttpServer.start();
            mTvServerBadge.setText("ONLINE :8080");
            mTvServerBadge.setBackgroundColor(0xFF2E7D32);
        } catch (Exception e) {
            Toast.makeText(this, "Lỗi khởi chạy Server cổng 8080: " + e.getMessage(), Toast.LENGTH_LONG).show();
        }
    }

    private boolean checkPermissions() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestPermissions() {
        ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.CAMERA}, PERMISSION_REQUEST_CODE);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions, @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PERMISSION_REQUEST_CODE) {
            if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                startCameraAndServer();
            } else {
                Toast.makeText(this, "Cần cấp quyền Camera để hoạt động!", Toast.LENGTH_LONG).show();
            }
        }
    }

    @Override
    public void onParametersChanged(String lensName, float ev, long exposureTimeNs, boolean autoExposure, String focusMode, float focusDistance, String iso, String whiteBalance, boolean torch) {
        runOnUiThread(() -> {
            mTvHudLens.setText("🔍 Ống kính: " + lensName);
            mTvHudEv.setText(String.format("EV: %+.1f", ev));

            if (autoExposure) {
                mTvHudExposure.setText("💡 Phơi sáng: Auto");
            } else {
                double us = exposureTimeNs / 1000.0;
                mTvHudExposure.setText(String.format("💡 Phơi sáng: %.0fµs (1/%.0fs)", us, (1_000_000_000.0 / exposureTimeNs)));
            }

            if ("manual".equalsIgnoreCase(focusMode)) {
                mTvHudFocus.setText(String.format("🎯 Nét: Manual (%.2f)", focusDistance));
            } else if ("macro".equalsIgnoreCase(focusMode)) {
                mTvHudFocus.setText("🎯 Nét: Macro");
            } else {
                mTvHudFocus.setText("🎯 Nét: Auto AF");
            }

            mTvHudIsoWb.setText("ISO: " + iso + " • WB: " + whiteBalance + (torch ? " • 💡 Flash ON" : ""));
        });
    }

    private void acquireWakeLock() {
        try {
            PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
            if (pm != null) {
                mWakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "VisionCameraServer::WakeLock");
                mWakeLock.acquire(12 * 60 * 60 * 1000L); // 12 hours
            }
        } catch (Exception ignored) {}
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        if (mHttpServer != null) {
            mHttpServer.stop();
        }
        if (mCameraManager != null) {
            mCameraManager.stop();
        }
        if (mWakeLock != null && mWakeLock.isHeld()) {
            try { mWakeLock.release(); } catch (Exception ignored) {}
        }
    }
}
