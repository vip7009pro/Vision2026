using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

class Program {
    static void Main() {
        var path = @"g:\NODEJS\Vision2026\VisionInspectionApp.VisionEngine\Class1.cs";
        var text = File.ReadAllText(path);

        var target = @"            if (imgL.Width < tplL.Width || imgL.Height < tplL.Height)
            {
                return (0.0, new Point(0, 0));
            }";

        var patch = @"            if (imgL.Width < tplL.Width || imgL.Height < tplL.Height)
            {
                var cw = Math.Min(tplL.Width, imgL.Width);
                var ch = Math.Min(tplL.Height, imgL.Height);
                var cx = (tplL.Width - cw) / 2;
                var cy = (tplL.Height - ch) / 2;
                var croppedTpl = new Mat(tplL, new Rect(cx, cy, cw, ch));
                tplL.Dispose();
                tplL = croppedTpl;
            }";

        if (text.Contains(target)) {
            text = text.Replace(target, patch);
            File.WriteAllText(path, text);
            Console.WriteLine("Replaced MatchTemplatePyramid");
        } else {
            Console.WriteLine("Not found");
        }
    }
}
