$content = Get-Content 'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml' -Encoding UTF8 -Raw
$content = $content.Replace('UpdateSourceTrigger=PropertyChanged}', 'UpdateSourceTrigger=PropertyChanged, Delay=500}')
$content = $content.Replace('UpdateSourceTrigger=PropertyChanged"', 'UpdateSourceTrigger=PropertyChanged, Delay=500"')
$content = $content.Replace('<TextBlock Text="{Binding Type}" Foreground="LightGray" FontSize="10" />', '<TextBlock Text="{Binding Type}" Foreground="LightGray" FontSize="10" />' + "`r`n" + '                                                            <TextBlock Text="{Binding ExecutionTimeMs, StringFormat=''{}Time: {0} ms''}" Foreground="LightBlue" FontSize="10" />')

$rowDefPattern = '<Grid\.RowDefinitions>\s*<RowDefinition Height="Auto" />\s*<RowDefinition Height="\*" />\s*</Grid\.RowDefinitions>'
$rowDefReplacement = '<Grid.RowDefinitions>' + "`r`n" + '            <RowDefinition Height="Auto" />' + "`r`n" + '            <RowDefinition Height="*" />' + "`r`n" + '            <RowDefinition Height="Auto" />' + "`r`n" + '        </Grid.RowDefinitions>'
$content = [System.Text.RegularExpressions.Regex]::Replace($content, $rowDefPattern, $rowDefReplacement)

$endGridPattern = '(?s)(</Grid>\s*</UserControl>)'
$endGridReplacement = '    <StatusBar Grid.Row="2" Background="{DynamicResource DarkBackgroundBrush}" Foreground="{DynamicResource TextBrush}" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource DarkBorderBrush}">' + "`r`n" + '        <StatusBarItem>' + "`r`n" + '            <TextBlock Text="{Binding StatusBarText}" />' + "`r`n" + '        </StatusBarItem>' + "`r`n" + '    </StatusBar>' + "`r`n" + '$1'
$content = [System.Text.RegularExpressions.Regex]::Replace($content, $endGridPattern, $endGridReplacement)

Set-Content 'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml' $content -Encoding UTF8
