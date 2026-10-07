$csvPath = "PLC_Programs/Mitsubishi_GXWorks3/DeviceComments_GXWorks.csv"
$rows = @(
    @('Device Name', 'Comment'),
    @('X0', 'Btn_System_Start'),
    @('X1', 'Btn_System_Stop'),
    @('X2', 'Sensor_Part_Chamber (Camera Trigger)'),
    @('X4', 'Btn_Worker_Reset_Start (Confirm NG fixed)'),
    @('Y0', 'Conveyor_Main_Run (Motor Y0)'),
    @('Y1', 'Camera_Hardware_Trigger_Line0'),
    @('Y21', 'NG_Stopper_Outside (Y21)'),
    @('Y22', 'NG_Alarm_Beacon_Buzzer (Y22)'),
    @('M10', 'PLC_Trigger_Bit (M10)'),
    @('M11', 'PLC_Ack_Bit (Optional M11)'),
    @('M20', 'Queue_Shift_Pulse (M20)'),
    @('M100', 'PC_Heartbeat_Watchdog (M100)'),
    @('M101', 'Vision_Ready (M101)'),
    @('M102', 'Vision_Busy (M102)'),
    @('M103', 'Vision_Done (M103)'),
    @('M104', 'Vision_Pass (M104)'),
    @('M105', 'Vision_NG (M105)'),
    @('M108', 'PLC_Heartbeat_Bit (SM410)'),
    @('M200', 'Queue_Slot_00 (Camera Chamber: 0=OK, 1=NG)'),
    @('M201', 'Queue_Slot_01 (After Chamber)'),
    @('M202', 'Queue_Slot_02 (In-Flight)'),
    @('M203', 'Queue_Slot_03 (In-Flight)'),
    @('M204', 'Queue_Slot_04 (In-Flight)'),
    @('M205', 'Queue_Slot_05 (Optional Stop Station)'),
    @('M206', 'Queue_Slot_06 (In-Flight)'),
    @('M207', 'Queue_Slot_07 (In-Flight)'),
    @('M208', 'Queue_Slot_08 (In-Flight)'),
    @('M209', 'Queue_Slot_09 (In-Flight)'),
    @('M210', 'Queue_Slot_10 (Target Stop Station NG)'),
    @('M211', 'Queue_Slot_11 (In-Flight)'),
    @('M212', 'Queue_Slot_12 (In-Flight)'),
    @('M213', 'Queue_Slot_13 (In-Flight)'),
    @('M214', 'Queue_Slot_14 (In-Flight)'),
    @('M215', 'Queue_Slot_15 (In-Flight)'),
    @('M216', 'Queue_Slot_16 (In-Flight)'),
    @('M217', 'Queue_Slot_17 (In-Flight)'),
    @('M218', 'Queue_Slot_18 (In-Flight)'),
    @('M219', 'Queue_Slot_19 (Queue End 20)'),
    @('M220', 'Conveyor_Stopped_NG_At_Station (M220)'),
    @('T0', 'Timer_Watchdog_Vision (300ms)'),
    @('D300', 'Total_Inspected_Count (DINT D300-301)'),
    @('D302', 'Total_Pass_Count (DINT D302-303)'),
    @('D304', 'Total_NG_Count (DINT D304-305)')
)

$sb = New-Object System.Text.StringBuilder
foreach ($r in $rows) {
    $sb.AppendLine(('"{0}"' -f $r[0]) + "`t" + ('"{0}"' -f $r[1])) | Out-Null
}
[System.IO.File]::WriteAllText((Resolve-Path $csvPath).Path, $sb.ToString(), [System.Text.Encoding]::Unicode)
Write-Host "Updated DeviceComments_GXWorks.csv successfully."
