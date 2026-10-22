$p = Join-Path $env:APPDATA 'Vision2026\plc_config.json'
if (Test-Path $p) {
    $raw = Get-Content $p -Raw
    $json = $raw | ConvertFrom-Json
    $json.IndustrialConfig.Handshake.PlcId = 'PLC_2'
    $json.IndustrialConfig.Handshake.ReadyTagName = 'M101'
    $json.IndustrialConfig.Handshake.BusyTagName = 'M102'
    $json.IndustrialConfig.Handshake.DoneTagName = 'M103'
    $json.IndustrialConfig.Handshake.PassTagName = 'M104'
    $json.IndustrialConfig.Handshake.NgTagName = 'M105'
    $json.IndustrialConfig.Handshake.PlcAckTagName = ''
    $json.IndustrialConfig.Handshake.PassSignalType = 1
    $json.IndustrialConfig.Handshake.NgSignalType = 1
    $json.IndustrialConfig.Handshake.DoneSignalType = 0
    $json.IndustrialConfig.Handshake.PulseDurationMs = 20
    $json.IndustrialConfig.Handshake.NonBlockingMode = $true
    $json.IndustrialConfig.Handshake.TargetStopStationIndex = 10
    $json.IndustrialConfig.Handshake.QueueRegisterStart = 'M200'
    $json.IndustrialConfig.Handshake.IsEnabled = $true

    $out = $json | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($p, $out, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($p + '.backup', $out, [System.Text.Encoding]::UTF8)
    Write-Host "Updated plc_config.json and created backup successfully."
}
