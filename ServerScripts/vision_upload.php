<?php
/**
 * CMS VINA VISION SYSTEM - REMOTE JOB & TEACH IMAGE UPLOAD API
 * -------------------------------------------------------------
 * Script này được triển khai trên máy chủ Web (Apache/XAMPP/Nginx/IIS).
 * Cung cấp API tải lên ảnh Teach Image và tệp Vision Job (.job),
 * phục vụ quản lý và huấn luyện (Teaching) Job từ xa.
 */

// Bật CORS cho phép ứng dụng desktop gọi API
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

header('Content-Type: application/json; charset=utf-8');

// Định nghĩa thư mục lưu trữ uploads
$baseUploadDir = __DIR__ . DIRECTORY_SEPARATOR . 'uploads';
$imageUploadDir = $baseUploadDir . DIRECTORY_SEPARATOR . 'teach_images';
$jobUploadDir = $baseUploadDir . DIRECTORY_SEPARATOR . 'jobs';

// Tự động tạo thư mục nếu chưa tồn tại
if (!is_dir($baseUploadDir)) {
    @mkdir($baseUploadDir, 0777, true);
}
if (!is_dir($imageUploadDir)) {
    @mkdir($imageUploadDir, 0777, true);
}
if (!is_dir($jobUploadDir)) {
    @mkdir($jobUploadDir, 0777, true);
}

// Xác định Base URL của server hiện tại
$protocol = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off' || $_SERVER['SERVER_PORT'] == 443) ? "https://" : "http://";
$host = $_SERVER['HTTP_HOST'];
$scriptDir = dirname($_SERVER['SCRIPT_NAME']);
$scriptDir = str_replace('\\', '/', $scriptDir);
if ($scriptDir === '/') {
    $scriptDir = '';
}
$baseUrl = $protocol . $host . $scriptDir;

$action = isset($_GET['action']) ? trim($_GET['action']) : (isset($_POST['action']) ? trim($_POST['action']) : 'ping');

// 1. ACTION: PING
if ($action === 'ping') {
    echo json_encode([
        'success' => true,
        'message' => 'CMS VINA Vision Upload Server is ONLINE.',
        'server_time' => date('Y-m-d H:i:s'),
        'base_url' => $baseUrl
    ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
    exit;
}

// 2. ACTION: UPLOAD_IMAGE (Tải lên ảnh Teach Image)
if ($action === 'upload_image') {
    if (!isset($_FILES['image_file']) && !isset($_FILES['file'])) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Không tìm thấy file ảnh đính kèm (image_file hoặc file).'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $file = isset($_FILES['image_file']) ? $_FILES['image_file'] : $_FILES['file'];
    if ($file['error'] !== UPLOAD_ERR_OK) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Lỗi tải tệp lên server. Mã lỗi: ' . $file['error']
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $productCode = isset($_POST['product_code']) ? preg_replace('/[^a-zA-Z0-9_\-]/', '_', trim($_POST['product_code'])) : 'PROD';
    if (empty($productCode)) {
        $productCode = 'PROD';
    }

    $originalName = $file['name'];
    $ext = strtolower(pathinfo($originalName, PATHINFO_EXTENSION));
    if (empty($ext) || !in_array($ext, ['png', 'jpg', 'jpeg', 'bmp'])) {
        $ext = 'png';
    }

    $fileName = 'teach_' . $productCode . '_' . date('Ymd_His') . '_' . bin2hex(random_bytes(3)) . '.' . $ext;
    $targetPath = $imageUploadDir . DIRECTORY_SEPARATOR . $fileName;

    if (move_uploaded_file($file['tmp_name'], $targetPath)) {
        $relativeUrl = 'uploads/teach_images/' . $fileName;
        $fullUrl = $baseUrl . '/' . $relativeUrl;
        
        echo json_encode([
            'success' => true,
            'message' => 'Tải ảnh Teach Image lên server thành công.',
            'url' => $fullUrl,
            'file_path' => $relativeUrl,
            'file_name' => $fileName,
            'size_bytes' => filesize($targetPath),
            'uploaded_at' => date('Y-m-d H:i:s')
        ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
        exit;
    } else {
        http_response_code(500);
        echo json_encode([
            'success' => false,
            'error' => 'Không thể lưu file ảnh vào thư mục đích.'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }
}

// 3. ACTION: UPLOAD_JOB (Tải lên tệp Vision Job .job)
if ($action === 'upload_job') {
    if (!isset($_FILES['job_file']) && !isset($_FILES['file'])) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Không tìm thấy file Job đính kèm (job_file hoặc file).'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $file = isset($_FILES['job_file']) ? $_FILES['job_file'] : $_FILES['file'];
    if ($file['error'] !== UPLOAD_ERR_OK) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Lỗi tải tệp lên server. Mã lỗi: ' . $file['error']
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $productCode = isset($_POST['product_code']) ? preg_replace('/[^a-zA-Z0-9_\-]/', '_', trim($_POST['product_code'])) : 'JOB';
    if (empty($productCode)) {
        $productCode = 'JOB';
    }

    $fileName = 'job_' . $productCode . '_' . date('Ymd_His') . '_' . bin2hex(random_bytes(3)) . '.job';
    $targetPath = $jobUploadDir . DIRECTORY_SEPARATOR . $fileName;

    if (move_uploaded_file($file['tmp_name'], $targetPath)) {
        $relativeUrl = 'uploads/jobs/' . $fileName;
        $fullUrl = $baseUrl . '/' . $relativeUrl;
        
        echo json_encode([
            'success' => true,
            'message' => 'Tải tệp Job lên server thành công.',
            'url' => $fullUrl,
            'file_path' => $relativeUrl,
            'file_name' => $fileName,
            'size_bytes' => filesize($targetPath),
            'uploaded_at' => date('Y-m-d H:i:s')
        ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
        exit;
    } else {
        http_response_code(500);
        echo json_encode([
            'success' => false,
            'error' => 'Không thể lưu file Job vào thư mục đích.'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }
}

// Default fallback
http_response_code(400);
echo json_encode([
    'success' => false,
    'error' => 'Action không hợp lệ. Các action hỗ trợ: ping, upload_image, upload_job.'
], JSON_UNESCAPED_UNICODE);
