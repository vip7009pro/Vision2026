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

/**
 * Chuyển đổi ký tự tiếng Việt có dấu sang không dấu
 */
function removeVietnameseDiacritics($str) {
    if (empty($str)) return '';
    $str = preg_replace("/(à|á|ạ|ả|ã|â|ầ|ấ|ậ|ẩ|ẫ|ă|ằ|ắ|ặ|ẳ|ẵ)/u", "a", $str);
    $str = preg_replace("/(è|é|ẹ|ẻ|ẽ|ê|ề|ế|ệ|ể|ễ)/u", "e", $str);
    $str = preg_replace("/(ì|í|ị|ỉ|ĩ)/u", "i", $str);
    $str = preg_replace("/(ò|ó|ọ|ỏ|õ|ô|ồ|ố|ộ|ổ|ỗ|ơ|ờ|ớ|ợ|ở|ỡ)/u", "o", $str);
    $str = preg_replace("/(ù|ú|ụ|ủ|ũ|ư|ừ|ứ|ự|ử|ữ)/u", "u", $str);
    $str = preg_replace("/(ỳ|ý|ỵ|ỷ|ỹ)/u", "y", $str);
    $str = preg_replace("/(đ)/u", "d", $str);
    $str = preg_replace("/(À|Á|Ạ|Ả|Ã|Â|Ầ|Ấ|Ậ|Ẩ|Ẫ|Ă|Ằ|Ắ|Ặ|Ẳ|Ẵ)/u", "A", $str);
    $str = preg_replace("/(È|É|Ẹ|Ẻ|Ẽ|Ê|Ề|Ế|Ệ|Ể|Ễ)/u", "E", $str);
    $str = preg_replace("/(Ì|Í|Ị|Ỉ|Ĩ)/u", "I", $str);
    $str = preg_replace("/(Ò|Ó|Ọ|Ỏ|Õ|Ô|Ồ|Ố|Ộ|Ổ|Ỗ|Ơ|Ờ|Ớ|Ợ|Ở|Ỡ)/u", "O", $str);
    $str = preg_replace("/(Ù|Ú|Ụ|Ủ|Ũ|Ư|Ừ|Ứ|Ự|Ử|Ữ)/u", "U", $str);
    $str = preg_replace("/(Ỳ|Ý|Ỵ|Ỷ|Ỹ)/u", "Y", $str);
    $str = preg_replace("/(Đ)/u", "D", $str);
    return $str;
}

/**
 * Chuẩn hóa mã/tên sản phẩm thành chuỗi định danh an toàn cho tên tệp
 */
function sanitizeIdentifier($str) {
    if (empty($str)) return '';
    $noAccent = removeVietnameseDiacritics(trim($str));
    $cleaned = preg_replace('/[^a-zA-Z0-9_\-]/', '_', $noAccent);
    $cleaned = preg_replace('/_+/', '_', trim($cleaned, '_'));
    return $cleaned;
}

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

    $productCode = isset($_POST['product_code']) ? sanitizeIdentifier($_POST['product_code']) : 'PROD';
    if (empty($productCode)) {
        $productCode = 'PROD';
    }
    $productName = isset($_POST['product_name']) ? sanitizeIdentifier($_POST['product_name']) : '';
    $identifier = !empty($productName) ? $productCode . '_' . $productName : $productCode;

    $originalName = $file['name'];
    $ext = strtolower(pathinfo($originalName, PATHINFO_EXTENSION));
    if (empty($ext) || !in_array($ext, ['png', 'jpg', 'jpeg', 'bmp'])) {
        $ext = 'png';
    }

    $fileName = 'teach_' . $identifier . '_' . date('Ymd_His') . '_' . bin2hex(random_bytes(3)) . '.' . $ext;
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

    $productCode = isset($_POST['product_code']) ? sanitizeIdentifier($_POST['product_code']) : 'JOB';
    if (empty($productCode)) {
        $productCode = 'JOB';
    }
    $productName = isset($_POST['product_name']) ? sanitizeIdentifier($_POST['product_name']) : '';
    $identifier = !empty($productName) ? $productCode . '_' . $productName : $productCode;

    $fileName = 'job_' . $identifier . '_' . date('Ymd_His') . '_' . bin2hex(random_bytes(3)) . '.job';
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
