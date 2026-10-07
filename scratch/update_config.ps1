$p = Join-Path $env:APPDATA 'Vision2026\plc_config.json'
if (Test-Path $p) {
    $raw = Get-Content $p -Raw
    $json = $raw | ConvertFrom-Json
    $json.IndustrialConfig.Handshake | Add-Member -NotePropertyName 'NonBlockingMode' -NotePropertyValue $true -Force
    $json.IndustrialConfig.Handshake | Add-Member -NotePropertyName 'TargetStopStationIndex' -NotePropertyValue 10 -Force
    $json.IndustrialConfig.Handshake | Add-Member -NotePropertyName 'QueueRegisterStart' -NotePropertyValue 'M200' -Force
    $out = $json | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($p, $out, [System.Text.Encoding]::UTF8)
    Write-Host "Updated plc_config.json successfully."
}
