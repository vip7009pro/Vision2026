# PowerShell Build and Install Script for VisionCameraServer (Samsung Galaxy S24 Ultra)
$ErrorActionPreference = "Stop"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "BUILDING VISION CAMERA SERVER FOR GALAXY S24 ULTRA" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Tim gradle.bat
$gradleBat = Get-ChildItem "C:\Users\Admin\.gradle\wrapper\dists" -Recurse -Filter "gradle.bat" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $gradleBat) {
    if (Get-Command "gradle" -ErrorAction SilentlyContinue) {
        $gradleBat = "gradle"
    } else {
        throw "Cannot find gradle.bat on system!"
    }
}

Write-Host "Using Gradle: $gradleBat" -ForegroundColor Yellow
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 2. Build APK
Write-Host "Compiling APK (assembleDebug)..." -ForegroundColor Yellow
& $gradleBat -p "$scriptDir" assembleDebug --no-daemon

$apkPath = Join-Path $scriptDir "app\build\outputs\apk\debug\app-debug.apk"
if (-not (Test-Path $apkPath)) {
    throw "APK not found after build: $apkPath"
}

Write-Host "APK compiled successfully: $apkPath" -ForegroundColor Green

# 3. Install to connected device via ADB
$adbDevices = adb devices | Where-Object { $_ -match "\tdevice" }
if ($adbDevices) {
    Write-Host "Installing to phone via ADB..." -ForegroundColor Cyan
    adb install -r "$apkPath"
    
    Write-Host "Granting CAMERA permission..." -ForegroundColor Cyan
    adb shell pm grant com.vision.cameraserver android.permission.CAMERA
    
    Write-Host "Starting VisionCameraServer app on Galaxy S24 Ultra..." -ForegroundColor Cyan
    adb shell am start -n com.vision.cameraserver/.MainActivity
    
    Write-Host "Port forwarding: adb forward tcp:8080 tcp:8080" -ForegroundColor Cyan
    adb forward tcp:8080 tcp:8080
    
    Write-Host ""
    Write-Host "SUCCESS! App is running on Galaxy S24 Ultra." -ForegroundColor Green
    Write-Host "PC can connect via: http://127.0.0.1:8080" -ForegroundColor Green
} else {
    Write-Host "No device connected via ADB. APK is ready at: $apkPath" -ForegroundColor Yellow
}
