<?php
/**
 * CMS VINA VISION SYSTEM - OTA UPDATE SERVER & PUBLISHER API
 * -------------------------------------------------------------
 * Script này được triển khai trên máy chủ Web (Apache / XAMPP / Nginx / IIS / cPanel).
 * Cung cấp các API:
 * 1. action=ping: Kiểm tra trạng thái máy chủ OTA.
 * 2. action=get_version (hoặc truy cập version.json): Trả về manifest bản cập nhật mới nhất cho các máy IPC.
 * 3. action=publish (hoặc action=upload): Tiếp nhận gói zip cập nhật tải lên 1 lần.
 * 4. action=upload_chunk: Tiếp nhận upload tệp zip phân đoạn (Chunked Upload),
 *    vượt qua hoàn toàn mọi giới hạn post_max_size và upload_max_filesize (kể cả 2M/8M/40M) của hosting!
 */

// Tắt hiển thị lỗi HTML để tránh làm hỏng định dạng phản hồi JSON của API
@ini_set('display_errors', '0');
error_reporting(E_ALL & ~E_WARNING & ~E_NOTICE);

// Tăng giới hạn thời gian thực thi và bộ nhớ nếu cấu hình máy chủ cho phép
@ini_set('memory_limit', '1024M');
@ini_set('max_execution_time', '900');
@ini_set('max_input_time', '900');

// Bật CORS cho phép ứng dụng desktop Windows WPF gọi API
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With, X-API-Key');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

header('Content-Type: application/json; charset=utf-8');

// BẮT LỖI SỚM: Kiểm tra trường hợp tệp gửi lên vượt quá post_max_size của PHP hosting
if (empty($_POST) && empty($_FILES) && isset($_SERVER['CONTENT_LENGTH']) && (int)$_SERVER['CONTENT_LENGTH'] > 0) {
    $postMax = ini_get('post_max_size');
    $uploadMax = ini_get('upload_max_filesize');
    http_response_code(400);
    echo json_encode([
        'success' => false,
        'error' => "Dung lượng gửi lên ({$_SERVER['CONTENT_LENGTH']} bytes) vượt quá giới hạn cấu hình post_max_size ({$postMax}) hoặc upload_max_filesize ({$uploadMax}) của máy chủ PHP. Ứng dụng sẽ tự động kích hoạt tính năng upload phân đoạn (Chunked Upload) để khắc phục.",
        'post_max_size' => $postMax,
        'upload_max_filesize' => $uploadMax,
        'received_bytes' => (int)$_SERVER['CONTENT_LENGTH']
    ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
    exit;
}

// Cấu hình Khóa bảo mật API Token (Để trống nếu không bắt buộc)
define('OTA_API_KEY', '');

// Thư mục gốc chứa các bản phát hành
$rootDir = __DIR__;
$defaultStorageFolder = 'uploads/ota_packages';
$tempDir = $rootDir . DIRECTORY_SEPARATOR . 'uploads' . DIRECTORY_SEPARATOR . 'temp';

if (!is_dir($tempDir)) {
    @mkdir($tempDir, 0777, true);
} else {
    // Dọn dẹp các phân đoạn tạm dang dở quá 2 giờ
    if ($dh = @opendir($tempDir)) {
        $now = time();
        while (($f = readdir($dh)) !== false) {
            if (substr($f, -5) === '.part') {
                $fp = $tempDir . DIRECTORY_SEPARATOR . $f;
                if ($now - filemtime($fp) > 7200) {
                    @unlink($fp);
                }
            }
        }
        closedir($dh);
    }
}

// Xác định Base URL của server hiện tại
$protocol = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off' || (isset($_SERVER['SERVER_PORT']) && $_SERVER['SERVER_PORT'] == 443)) ? "https://" : "http://";
$host = isset($_SERVER['HTTP_HOST']) ? $_SERVER['HTTP_HOST'] : 'localhost';
$scriptDir = dirname($_SERVER['SCRIPT_NAME']);
$scriptDir = str_replace('\\', '/', $scriptDir);
if ($scriptDir === '/' || $scriptDir === '.') {
    $scriptDir = '';
}
$baseUrl = rtrim($protocol . $host . $scriptDir, '/');

// Lấy action từ GET hoặc POST
$action = isset($_GET['action']) ? trim($_GET['action']) : (isset($_POST['action']) ? trim($_POST['action']) : 'ping');

/**
 * Chuẩn hóa và làm sạch đường dẫn thư mục lưu trữ trên server
 * Ngăn chặn Directory Traversal (tấn công ../..)
 */
function sanitizeServerFolder($folderPath, $defaultFolder) {
    if (empty($folderPath)) {
        return $defaultFolder;
    }
    $normalized = str_replace('\\', '/', trim($folderPath));
    $parts = explode('/', $normalized);
    $safeParts = [];
    foreach ($parts as $part) {
        $part = trim($part);
        if ($part === '' || $part === '.' || $part === '..') {
            continue;
        }
        $cleaned = preg_replace('/[^a-zA-Z0-9_\-\.]/', '_', $part);
        if (!empty($cleaned)) {
            $safeParts[] = $cleaned;
        }
    }
    if (empty($safeParts)) {
        return $defaultFolder;
    }
    return implode('/', $safeParts);
}

// 1. ACTION: PING
if ($action === 'ping') {
    $versionFile = $rootDir . DIRECTORY_SEPARATOR . 'version.json';
    $hasVersionFile = file_exists($versionFile);
    $currentManifest = null;
    if ($hasVersionFile) {
        $raw = @file_get_contents($versionFile);
        $currentManifest = json_decode($raw, true);
    }

    echo json_encode([
        'success' => true,
        'message' => 'CMS VINA OTA Update Server is ONLINE.',
        'server_time' => date('Y-m-d H:i:s'),
        'base_url' => $baseUrl,
        'post_max_size' => ini_get('post_max_size'),
        'upload_max_filesize' => ini_get('upload_max_filesize'),
        'chunked_upload_supported' => true,
        'latest_version' => isset($currentManifest['Version']) ? $currentManifest['Version'] : null,
        'has_active_release' => $hasVersionFile
    ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
    exit;
}

// 2. ACTION: GET_VERSION (Cung cấp nội dung version.json cho các máy IPC kiểm tra OTA)
if ($action === 'get_version' || $action === 'manifest') {
    $versionFile = $rootDir . DIRECTORY_SEPARATOR . 'version.json';
    if (!file_exists($versionFile)) {
        http_response_code(404);
        echo json_encode([
            'success' => false,
            'error' => 'Chưa có bản phát hành nào được đăng tải (Không tìm thấy tệp version.json).'
        ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
        exit;
    }

    $content = @file_get_contents($versionFile);
    if ($content === false) {
        http_response_code(500);
        echo json_encode(['success' => false, 'error' => 'Không thể đọc tệp version.json trên máy chủ.'], JSON_UNESCAPED_UNICODE);
        exit;
    }

    echo $content;
    exit;
}

/**
 * Hàm kiểm tra khóa bí mật API Token
 */
function verifyApiToken() {
    if (defined('OTA_API_KEY') && OTA_API_KEY !== '') {
        $clientKey = isset($_POST['api_token']) ? trim($_POST['api_token']) : '';
        if (empty($clientKey) && isset($_SERVER['HTTP_X_API_KEY'])) {
            $clientKey = trim($_SERVER['HTTP_X_API_KEY']);
        }
        if ($clientKey !== OTA_API_KEY) {
            http_response_code(401);
            echo json_encode([
                'success' => false,
                'error' => 'Khóa xác thực API Token không chính xác hoặc chưa được cung cấp.'
            ], JSON_UNESCAPED_UNICODE);
            exit;
        }
    }
}

// 3. ACTION: UPLOAD_CHUNK (Tải lên phân đoạn - Chunked Upload vượt giới hạn post_max_size)
if ($action === 'upload_chunk' || $action === 'chunk') {
    verifyApiToken();

    $fileId = isset($_POST['file_id']) ? preg_replace('/[^a-zA-Z0-9_\-]/', '', $_POST['file_id']) : '';
    if (empty($fileId)) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => 'Thiếu tham số file_id xác định phiên upload.'], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $chunkIndex = isset($_POST['chunk_index']) ? (int)$_POST['chunk_index'] : -1;
    $totalChunks = isset($_POST['total_chunks']) ? (int)$_POST['total_chunks'] : -1;

    if ($chunkIndex < 0 || $totalChunks <= 0 || $chunkIndex >= $totalChunks) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => 'Chỉ số phân đoạn (chunk_index / total_chunks) không hợp lệ.'], JSON_UNESCAPED_UNICODE);
        exit;
    }

    if (!isset($_FILES['package_chunk']) && !isset($_FILES['file'])) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => 'Không tìm thấy dữ liệu phân đoạn (package_chunk hoặc file).'], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $chunkFile = isset($_FILES['package_chunk']) ? $_FILES['package_chunk'] : $_FILES['file'];
    if ($chunkFile['error'] !== UPLOAD_ERR_OK) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => 'Lỗi nhận phân đoạn. Mã lỗi PHP: ' . $chunkFile['error']], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $tempPartFile = $tempDir . DIRECTORY_SEPARATOR . 'upload_' . $fileId . '.part';

    // Ghép phân đoạn vào tệp tạm
    $in = @fopen($chunkFile['tmp_name'], 'rb');
    $out = @fopen($tempPartFile, $chunkIndex === 0 ? 'wb' : 'ab');
    if ($in && $out) {
        while ($buff = fread($in, 81920)) {
            fwrite($out, $buff);
        }
        fclose($in);
        fclose($out);
    } else {
        if ($in) fclose($in);
        if ($out) fclose($out);
        http_response_code(500);
        echo json_encode(['success' => false, 'error' => 'Không thể ghi phân đoạn vào tệp tạm trên server.'], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // Nếu chưa phải phân đoạn cuối cùng -> Báo nhận thành công phân đoạn
    if ($chunkIndex < $totalChunks - 1) {
        echo json_encode([
            'success' => true,
            'message' => "Đã nhận thành công phân đoạn " . ($chunkIndex + 1) . "/{$totalChunks}.",
            'chunk_index' => $chunkIndex,
            'total_chunks' => $totalChunks,
            'is_finished' => false
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // ĐÃ NHẬN ĐỦ TOÀN BỘ PHÂN ĐOẠN -> Hoàn tất ghép file và xuất bản bản cập nhật!
    finalizePublishedPackage($tempPartFile, $rootDir, $defaultStorageFolder, $baseUrl);
    exit;
}

// 4. ACTION: PUBLISH / UPLOAD (Tải lên gói cập nhật 1 lần thông thường)
if ($action === 'publish' || $action === 'upload' || $action === 'upload_package') {
    verifyApiToken();

    if (!isset($_FILES['package_file']) && !isset($_FILES['file'])) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Không tìm thấy tệp gói cập nhật (.zip) trong yêu cầu (field: package_file hoặc file).'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $uploadedFile = isset($_FILES['package_file']) ? $_FILES['package_file'] : $_FILES['file'];
    if ($uploadedFile['error'] !== UPLOAD_ERR_OK) {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Lỗi truyền tải tệp cập nhật lên máy chủ. Mã lỗi PHP: ' . $uploadedFile['error']
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    $ext = strtolower(pathinfo($uploadedFile['name'], PATHINFO_EXTENSION));
    if ($ext !== 'zip') {
        http_response_code(400);
        echo json_encode([
            'success' => false,
            'error' => 'Tệp cập nhật không hợp lệ. Chỉ chấp nhận tệp nén định dạng .zip.'
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    finalizePublishedPackage($uploadedFile['tmp_name'], $rootDir, $defaultStorageFolder, $baseUrl, true);
    exit;
}

/**
 * Xử lý lưu trữ tệp zip hoàn chỉnh, tính SHA256 và cập nhật version.json
 */
function finalizePublishedPackage($sourceFilePath, $rootDir, $defaultStorageFolder, $baseUrl, $isUploadedFile = false) {
    // Xử lý thư mục lưu trữ theo cấu hình người dùng
    $requestedFolder = isset($_POST['target_dir']) ? $_POST['target_dir'] : (isset($_POST['folder']) ? $_POST['folder'] : $defaultStorageFolder);
    $safeRelativeFolder = sanitizeServerFolder($requestedFolder, $defaultStorageFolder);
    $absoluteTargetDir = $rootDir . DIRECTORY_SEPARATOR . str_replace('/', DIRECTORY_SEPARATOR, $safeRelativeFolder);

    if (!is_dir($absoluteTargetDir)) {
        if (!@mkdir($absoluteTargetDir, 0777, true)) {
            http_response_code(500);
            echo json_encode([
                'success' => false,
                'error' => "Không thể tạo thư mục lưu trữ trên server: '{$safeRelativeFolder}'. Vui lòng kiểm tra quyền ghi tệp."
            ], JSON_UNESCAPED_UNICODE);
            exit;
        }
    }

    // Thu thập dữ liệu Manifest / Phiên bản
    $manifestData = [];
    if (isset($_POST['manifest_json']) && !empty($_POST['manifest_json'])) {
        $decoded = json_decode($_POST['manifest_json'], true);
        if (is_array($decoded)) {
            $manifestData = $decoded;
        }
    } elseif (isset($_FILES['manifest_file']) && $_FILES['manifest_file']['error'] === UPLOAD_ERR_OK) {
        $rawManifest = @file_get_contents($_FILES['manifest_file']['tmp_name']);
        if ($rawManifest !== false) {
            $decoded = json_decode($rawManifest, true);
            if (is_array($decoded)) {
                $manifestData = $decoded;
            }
        }
    }

    $version = isset($_POST['version']) && !empty($_POST['version']) 
        ? trim($_POST['version']) 
        : (isset($manifestData['Version']) ? trim($manifestData['Version']) : '1.0.0.1');

    $appName = isset($_POST['app_name']) 
        ? trim($_POST['app_name']) 
        : (isset($manifestData['AppName']) ? trim($manifestData['AppName']) : 'CMS VINA Vision System');

    $channel = isset($_POST['channel']) 
        ? trim($_POST['channel']) 
        : (isset($manifestData['Channel']) ? trim($manifestData['Channel']) : 'Stable');

    $releaseNotes = isset($_POST['release_notes']) 
        ? trim($_POST['release_notes']) 
        : (isset($manifestData['ReleaseNotes']) ? trim($manifestData['ReleaseNotes']) : 'Bản cập nhật tính năng mới.');

    $isMandatory = isset($_POST['is_mandatory']) 
        ? filter_var($_POST['is_mandatory'], FILTER_VALIDATE_BOOLEAN) 
        : (isset($manifestData['IsMandatory']) ? (bool)$manifestData['IsMandatory'] : false);

    $minSupportedVersion = isset($_POST['min_supported_version']) 
        ? trim($_POST['min_supported_version']) 
        : (isset($manifestData['MinSupportedVersion']) ? trim($manifestData['MinSupportedVersion']) : null);

    $cleanVersionTag = preg_replace('/[^0-9\.]/', '', $version);
    if (empty($cleanVersionTag)) {
        $cleanVersionTag = '1.0.0.1';
    }
    $destinationFileName = "VisionUpdate_v{$cleanVersionTag}_" . date('Ymd_His') . ".zip";
    $destinationFilePath = $absoluteTargetDir . DIRECTORY_SEPARATOR . $destinationFileName;

    // Di chuyển tệp
    $moveSuccess = false;
    if ($isUploadedFile) {
        $moveSuccess = @move_uploaded_file($sourceFilePath, $destinationFilePath);
    } else {
        $moveSuccess = @rename($sourceFilePath, $destinationFilePath);
        if (!$moveSuccess) {
            $moveSuccess = @copy($sourceFilePath, $destinationFilePath);
            @unlink($sourceFilePath);
        }
    }

    if (!$moveSuccess || !file_exists($destinationFilePath)) {
        http_response_code(500);
        echo json_encode([
            'success' => false,
            'error' => "Không thể lưu tệp zip vào đường dẫn đích trên máy chủ: '{$destinationFilePath}'."
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // Tính kích thước và SHA-256 thực tế
    $fileSizeBytes = filesize($destinationFilePath);
    $sha256Hash = hash_file('sha256', $destinationFilePath);

    // URL tải về công khai
    $downloadUrl = $baseUrl . '/' . $safeRelativeFolder . '/' . $destinationFileName;

    // Cập nhật version.json
    $finalManifest = [
        'AppName' => $appName,
        'Version' => $cleanVersionTag,
        'ReleaseDate' => date('Y-m-d\TH:i:s\Z'),
        'Channel' => $channel,
        'IsMandatory' => $isMandatory,
        'MinSupportedVersion' => !empty($minSupportedVersion) ? $minSupportedVersion : null,
        'DownloadUrl' => $downloadUrl,
        'FileSize' => $fileSizeBytes,
        'Sha256' => $sha256Hash,
        'ReleaseNotes' => $releaseNotes
    ];

    $manifestJsonFormatted = json_encode($finalManifest, JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);

    // Ghi file ở webroot
    $rootVersionFile = $rootDir . DIRECTORY_SEPARATOR . 'version.json';
    @file_put_contents($rootVersionFile, $manifestJsonFormatted, LOCK_EX);

    // Ghi file bản sao ở thư mục gói zip
    $folderVersionFile = $absoluteTargetDir . DIRECTORY_SEPARATOR . 'version.json';
    @file_put_contents($folderVersionFile, $manifestJsonFormatted, LOCK_EX);

    echo json_encode([
        'success' => true,
        'message' => "Phát hành bản cập nhật v{$cleanVersionTag} lên máy chủ thành công!",
        'version' => $cleanVersionTag,
        'download_url' => $downloadUrl,
        'manifest_url' => $baseUrl . '/version.json',
        'server_folder' => $safeRelativeFolder,
        'file_name' => $destinationFileName,
        'file_size' => $fileSizeBytes,
        'sha256' => $sha256Hash,
        'release_date' => $finalManifest['ReleaseDate'],
        'manifest' => $finalManifest
    ], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
}

// Fallback: Action không hợp lệ
http_response_code(400);
echo json_encode([
    'success' => false,
    'error' => 'Hành động (Action) không hợp lệ. Các action được hỗ trợ: ping, get_version, publish, upload_chunk.'
], JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
