$p = "VisionInspectionApp.UI\bin\x64\Debug\net8.0-windows\MvCameraControl.Net.dll"
if (Test-Path $p) {
    $asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $p))
    $asm.GetTypes() | Where-Object { $_.Name -like "*Camera*" -or $_.Name -like "*PIXEL*" -or $_.Name -like "*FRAME*" } | ForEach-Object {
        Write-Host "TYPE: " $_.FullName
        $_.GetMethods() | Where-Object { $_.Name -like "*Convert*" -or $_.Name -like "*Pixel*" -or $_.Name -like "*Bayer*" -or $_.Name -like "*Image*" } | ForEach-Object {
            Write-Host "  METHOD: " $_.ToString()
        }
        $_.GetFields() | ForEach-Object {
            Write-Host "  FIELD: " $_.ToString()
        }
    }
}
