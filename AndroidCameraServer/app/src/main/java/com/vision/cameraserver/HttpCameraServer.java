package com.vision.cameraserver;

import android.util.Log;

import java.io.*;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class HttpCameraServer {
    private static final String TAG = "HttpCameraServer";

    private final int mPort;
    private final Camera2Manager mCameraManager;
    private ServerSocket mServerSocket;
    private ExecutorService mThreadPool;
    private volatile boolean mIsRunning = false;

    public HttpCameraServer(int port, Camera2Manager cameraManager) {
        mPort = port;
        mCameraManager = cameraManager;
    }

    public synchronized void start() throws IOException {
        if (mIsRunning) return;

        mServerSocket = new ServerSocket(mPort);
        mServerSocket.setReuseAddress(true);
        mThreadPool = Executors.newCachedThreadPool();
        mIsRunning = true;

        mThreadPool.execute(this::acceptLoop);
        Log.i(TAG, "HttpCameraServer started on port: " + mPort);
    }

    public synchronized void stop() {
        if (!mIsRunning) return;

        mIsRunning = false;
        try {
            if (mServerSocket != null && !mServerSocket.isClosed()) {
                mServerSocket.close();
            }
        } catch (Exception ignored) {}

        if (mThreadPool != null) {
            mThreadPool.shutdownNow();
            mThreadPool = null;
        }
        Log.i(TAG, "HttpCameraServer stopped.");
    }

    public boolean isRunning() {
        return mIsRunning;
    }

    private void acceptLoop() {
        while (mIsRunning && mServerSocket != null && !mServerSocket.isClosed()) {
            try {
                Socket clientSocket = mServerSocket.accept();
                clientSocket.setTcpNoDelay(true);
                mThreadPool.execute(() -> handleClient(clientSocket));
            } catch (Exception e) {
                if (!mIsRunning) break;
                Log.e(TAG, "Accept error", e);
            }
        }
    }

    private void handleClient(Socket socket) {
        try (InputStream in = socket.getInputStream();
             OutputStream out = socket.getOutputStream()) {

            BufferedReader reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8));
            String requestLine = reader.readLine();
            if (requestLine == null || requestLine.isEmpty()) return;

            String[] parts = requestLine.split(" ");
            if (parts.length < 2) return;

            String method = parts[0];
            String fullUri = parts[1];

            // Parse URL path and query parameters
            String path = fullUri;
            Map<String, String> queryParams = new HashMap<>();
            int queryIdx = fullUri.indexOf('?');
            if (queryIdx >= 0) {
                path = fullUri.substring(0, queryIdx);
                String queryString = fullUri.substring(queryIdx + 1);
                for (String param : queryString.split("&")) {
                    String[] pair = param.split("=");
                    if (pair.length == 2) {
                        queryParams.put(URLDecoder.decode(pair[0], "UTF-8"), URLDecoder.decode(pair[1], "UTF-8"));
                    } else if (pair.length == 1) {
                        queryParams.put(URLDecoder.decode(pair[0], "UTF-8"), "");
                    }
                }
            }

            routeRequest(path, queryParams, out);

        } catch (Exception ignored) {
        } finally {
            try { socket.close(); } catch (Exception ignored) {}
        }
    }

    private void routeRequest(String path, Map<String, String> params, OutputStream out) throws IOException {
        // 1. Luồng video MJPEG
        if ("/video".equals(path) || "/mjpeg".equals(path)) {
            sendMjpegStream(out);
            return;
        }

        // 2. Lấy frame xem trước mới nhất
        if ("/shot.jpg".equals(path) || "/preview.jpg".equals(path)) {
            byte[] jpeg = mCameraManager.getLatestPreviewJpeg();
            if (jpeg != null) {
                sendJpegResponse(out, jpeg);
            } else {
                send404(out, "Frame not ready yet");
            }
            return;
        }

        // 3. Chụp ảnh tĩnh Full-Res Snapshot qua Camera2
        if ("/photo.jpg".equals(path) || "/snapshot.jpg".equals(path)) {
            byte[] fullRes = mCameraManager.takeFullResSnapshot(2500);
            if (fullRes != null) {
                sendJpegResponse(out, fullRes);
            } else {
                send404(out, "Snapshot failed");
            }
            return;
        }

        // 4. Trạng thái JSON
        if ("/status.json".equals(path)) {
            sendStatusJson(out);
            return;
        }

        // 5. Điều khiển Đèn Flash Torch
        if ("/enabletorch".equals(path)) {
            mCameraManager.setTorch(true);
            sendOkText(out, "OK: torch=on");
            return;
        }
        if ("/disabletorch".equals(path)) {
            mCameraManager.setTorch(false);
            sendOkText(out, "OK: torch=off");
            return;
        }

        // 6. Cấu hình thông số qua /settings/...
        if (path.startsWith("/settings/")) {
            handleSettings(path, params, out);
            return;
        }

        // 7. Trang chủ Web Dashboard
        if ("/".equals(path) || "/index.html".equals(path)) {
            sendHtmlDashboard(out);
            return;
        }

        send404(out, "Endpoint not found: " + path);
    }

    private void handleSettings(String path, Map<String, String> params, OutputStream out) throws IOException {
        String setVal = params.get("set");

        // Đổi ống kính (0: 1x 200MP, 1: Tele 3x, 2: Tele 5x, 3: UltraWide 0.6x, 4: Front)
        if ("/settings/camera".equals(path) && setVal != null) {
            try {
                int lensId = Integer.parseInt(setVal.trim());
                mCameraManager.setLens(lensId);
                sendOkText(out, "OK: camera=" + lensId);
                return;
            } catch (Exception ignored) {}
        }

        // Bù trừ sáng EV
        if ("/settings/exposure_compensation".equals(path) && setVal != null) {
            try {
                float ev = Float.parseFloat(setVal.trim());
                mCameraManager.setEv(ev);
                sendOkText(out, "OK: exposure_compensation=" + ev);
                return;
            } catch (Exception ignored) {}
        }

        // Chế độ phơi sáng Auto / Manual
        if ("/settings/exposure_mode".equals(path) && setVal != null) {
            boolean auto = setVal.equalsIgnoreCase("auto");
            mCameraManager.setAutoExposure(auto);
            sendOkText(out, "OK: exposure_mode=" + (auto ? "auto" : "manual"));
            return;
        }

        // Thời gian phơi sáng Shutter Speed
        if ("/settings/exposure_time".equals(path) && setVal != null) {
            try {
                long ns = Long.parseLong(setVal.trim());
                mCameraManager.setExposureTimeNs(ns);
                sendOkText(out, "OK: exposure_time=" + ns);
                return;
            } catch (Exception ignored) {}
        }

        // Chế độ lấy nét (continuous-picture, macro, manual)
        if ("/settings/focus_mode".equals(path) && setVal != null) {
            mCameraManager.setFocusMode(setVal.trim());
            sendOkText(out, "OK: focus_mode=" + setVal);
            return;
        }

        // Cự ly lấy nét tay (0.0 -> 1.0)
        if ("/settings/focus_distance".equals(path) && setVal != null) {
            try {
                float dist = Float.parseFloat(setVal.trim());
                mCameraManager.setFocusDistanceRatio(dist);
                sendOkText(out, "OK: focus_distance=" + dist);
                return;
            } catch (Exception ignored) {}
        }

        // ISO
        if ("/settings/iso".equals(path) && setVal != null) {
            mCameraManager.setIso(setVal.trim());
            sendOkText(out, "OK: iso=" + setVal);
            return;
        }

        // White Balance
        if ("/settings/whitebalance".equals(path) && setVal != null) {
            mCameraManager.setWhiteBalance(setVal.trim());
            sendOkText(out, "OK: whitebalance=" + setVal);
            return;
        }

        sendOkText(out, "OK: acknowledged");
    }

    private void sendMjpegStream(OutputStream out) throws IOException {
        String header = "HTTP/1.0 200 OK\r\n" +
                "Server: VisionCameraServer\r\n" +
                "Connection: close\r\n" +
                "Max-Age: 0\r\n" +
                "Expires: 0\r\n" +
                "Cache-Control: no-cache, private\r\n" +
                "Pragma: no-cache\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=--frame\r\n\r\n";

        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.flush();

        Object frameLock = mCameraManager.getFrameLock();
        byte[] lastSent = null;

        while (mIsRunning) {
            byte[] current;
            synchronized (frameLock) {
                try {
                    frameLock.wait(100);
                } catch (InterruptedException e) {
                    break;
                }
                current = mCameraManager.getLatestPreviewJpeg();
            }

            if (current != null && current != lastSent) {
                lastSent = current;
                String partHeader = "--frame\r\n" +
                        "Content-Type: image/jpeg\r\n" +
                        "Content-Length: " + current.length + "\r\n\r\n";

                out.write(partHeader.getBytes(StandardCharsets.UTF_8));
                out.write(current);
                out.write("\r\n".getBytes(StandardCharsets.UTF_8));
                out.flush();
            }
        }
    }

    private void sendJpegResponse(OutputStream out, byte[] jpeg) throws IOException {
        String header = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: image/jpeg\r\n" +
                "Content-Length: " + jpeg.length + "\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: close\r\n\r\n";
        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.write(jpeg);
        out.flush();
    }

    private void sendStatusJson(OutputStream out) throws IOException {
        String json = "{\n" +
                "  \"curvals\": {\n" +
                "    \"camera\": " + mCameraManager.getCurrentLensIndex() + ",\n" +
                "    \"exposure_compensation\": " + String.format(java.util.Locale.US, "%.2f", mCameraManager.getEv()) + ",\n" +
                "    \"exposure_mode\": \"" + (mCameraManager.isAutoExposure() ? "auto" : "manual") + "\",\n" +
                "    \"exposure_time\": " + mCameraManager.getExposureTimeNs() + ",\n" +
                "    \"focus_mode\": \"" + mCameraManager.getFocusMode() + "\",\n" +
                "    \"focus_distance\": " + String.format(java.util.Locale.US, "%.2f", mCameraManager.getFocusDistanceRatio()) + ",\n" +
                "    \"iso\": \"" + mCameraManager.getIso() + "\",\n" +
                "    \"whitebalance\": \"" + mCameraManager.getWhiteBalance() + "\",\n" +
                "    \"torch\": " + mCameraManager.isTorchEnabled() + "\n" +
                "  }\n" +
                "}";

        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        String header = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json; charset=UTF-8\r\n" +
                "Content-Length: " + body.length + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";
        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.write(body);
        out.flush();
    }

    private void sendHtmlDashboard(OutputStream out) throws IOException {
        String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
                "<title>Vision Camera Server - Galaxy S24 Ultra</title>" +
                "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
                "<style>body{background:#121820;color:#F1F5F9;font-family:sans-serif;text-align:center;padding:20px;}" +
                "img{max-width:100%;border-radius:8px;box-shadow:0 4px 12px rgba(0,0,0,0.5);margin:15px 0;}" +
                ".badge{background:#10B981;padding:4px 12px;border-radius:12px;font-weight:bold;font-size:13px;}" +
                ".card{background:#1E2632;padding:15px;border-radius:8px;max-width:640px;margin:15px auto;text-align:left;}" +
                "</style></head><body>" +
                "<h2>📱 Vision Camera Server (Galaxy S24 Ultra)</h2>" +
                "<span class='badge'>ONLINE :8080</span>" +
                "<div class='card'>" +
                "<b>Live MJPEG Preview:</b><br>" +
                "<img src='/video' width='640'><br>" +
                "<b>Endpoints:</b><br>" +
                "• <a href='/video' style='color:#00E5FF'>/video</a> (Live MJPEG Stream)<br>" +
                "• <a href='/shot.jpg' style='color:#00E5FF'>/shot.jpg</a> (Latest Frame)<br>" +
                "• <a href='/photo.jpg' style='color:#00E5FF'>/photo.jpg</a> (Full-Res Sensor Snapshot)<br>" +
                "• <a href='/status.json' style='color:#00E5FF'>/status.json</a> (Current Parameters)<br>" +
                "</div>" +
                "</body></html>";

        byte[] body = html.getBytes(StandardCharsets.UTF_8);
        String header = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=UTF-8\r\n" +
                "Content-Length: " + body.length + "\r\n" +
                "Connection: close\r\n\r\n";
        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.write(body);
        out.flush();
    }

    private void sendOkText(OutputStream out, String text) throws IOException {
        byte[] body = text.getBytes(StandardCharsets.UTF_8);
        String header = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/plain; charset=UTF-8\r\n" +
                "Content-Length: " + body.length + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";
        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.write(body);
        out.flush();
    }

    private void send404(OutputStream out, String error) throws IOException {
        byte[] body = error.getBytes(StandardCharsets.UTF_8);
        String header = "HTTP/1.1 404 Not Found\r\n" +
                "Content-Type: text/plain; charset=UTF-8\r\n" +
                "Content-Length: " + body.length + "\r\n" +
                "Connection: close\r\n\r\n";
        out.write(header.getBytes(StandardCharsets.UTF_8));
        out.write(body);
        out.flush();
    }
}
