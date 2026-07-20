using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        var path = @"g:\NODEJS\Vision2026\VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs";
        var content = File.ReadAllText(path);
        
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("dst.Add(new OverlayRectItem"))
            {
                // Check if the next few lines match our pattern
                if (i + 7 < lines.Length && 
                    lines[i+1].Contains("{") &&
                    lines[i+2].Contains("X = ") && lines[i+2].Contains(".X,") &&
                    lines[i+3].Contains("Y = ") && lines[i+3].Contains(".Y,") &&
                    lines[i+4].Contains("Width = ") && lines[i+4].Contains(".Width,") &&
                    lines[i+5].Contains("Height = ") && lines[i+5].Contains(".Height,") &&
                    lines[i+6].Contains("Stroke = ") &&
                    lines[i+7].Contains("Label = ") &&
                    lines[i+8].Contains("});"))
                {
                    var xLine = lines[i+2].Trim();
                    var match = Regex.Match(xLine, @"X\s*=\s*([a-zA-Z0-9_\.]+)\.X");
                    if (match.Success)
                    {
                        var varName = match.Groups[1].Value;
                        var strokeMatch = Regex.Match(lines[i+6], @"Stroke\s*=\s*(.*?),");
                        var stroke = strokeMatch.Groups[1].Value;
                        var labelMatch = Regex.Match(lines[i+7], @"Label\s*=\s*(.*)");
                        var label = labelMatch.Groups[1].Value;
                        
                        // Replace the lines
                        lines[i] = lines[i].Replace("dst.Add(new OverlayRectItem", $"dst.Add(CreateRotatedRoi({varName}, {stroke}, {label}");
                        lines[i] = lines[i] + "));"; // Added 2nd closing parenthesis here!
                        
                        // Clear the replaced lines
                        for (int j = 1; j <= 8; j++)
                        {
                            lines[i+j] = "";
                        }
                    }
                }
                else if (i + 6 < lines.Length &&
                    lines[i+1].Contains("{") &&
                    lines[i+2].Contains("X = ") && lines[i+2].Contains(".X,") &&
                    lines[i+3].Contains("Y = ") && lines[i+3].Contains(".Y,") &&
                    lines[i+4].Contains("Width = ") && lines[i+4].Contains(".Width,") &&
                    lines[i+5].Contains("Height = ") && lines[i+5].Contains(".Height,") &&
                    lines[i+6].Contains("});"))
                {
                    // No Stroke or Label
                    var xLine = lines[i+2].Trim();
                    var match = Regex.Match(xLine, @"X\s*=\s*([a-zA-Z0-9_\.]+)\.X");
                    if (match.Success)
                    {
                        var varName = match.Groups[1].Value;
                        lines[i] = lines[i].Replace("dst.Add(new OverlayRectItem", $"dst.Add(CreateRotatedRoi({varName}, null, null));");
                        for (int j = 1; j <= 6; j++) lines[i+j] = "";
                    }
                }
            }
        }
        
        // write it back, filtering out empty lines where we cleared
        using (var sw = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "")
                {
                    sw.WriteLine(lines[i]);
                }
            }
        }
    }
}
