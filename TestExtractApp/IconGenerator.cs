using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace TestExtractApp;

public static class IconGenerator
{
    public static void GenerateAppIcons()
    {
        string logoPath = @"G:\NODEJS\Vision2026\VisionInspectionApp.UI\Assets\cms_vina_logo.png";
        string outputDir = @"G:\NODEJS\Vision2026\TestExtractApp\IconsOutput";
        string targetIcoPath = @"G:\NODEJS\Vision2026\VisionInspectionApp.UI\Assets\cms-vina-vision-system.ico";
        Directory.CreateDirectory(outputDir);

        if (!File.Exists(logoPath))
        {
            Console.WriteLine("Logo file not found: " + logoPath);
            return;
        }

        using var srcLogo = new Bitmap(logoPath);

        // Render Design: Ultra-Clean Vision Reticle Light (Windows 11 Squircle)
        using var bmpReticleLight = RenderDesignReticleLight(srcLogo, 512);
        bmpReticleLight.Save(Path.Combine(outputDir, "design_reticle_light_512.png"), ImageFormat.Png);

        // Save as standard Windows PE compliant multi-resolution ICO file
        CreateStandardWindowsIco(bmpReticleLight, targetIcoPath);

        Console.WriteLine("✅ Standard Windows PE Multi-Resolution ICO saved to " + targetIcoPath);
    }

    private static Bitmap RenderDesignReticleLight(Bitmap srcLogo, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        ConfigureGraphics(g);

        float margin = size * 0.04f;
        float cornerRadius = size * 0.22f;
        var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

        using (var path = GetRoundedRectPath(rect, cornerRadius))
        {
            // Drop shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 1, margin + size * 0.025f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Crisp Pure White to Soft Blue/Silver gradient
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 235, 244, 253), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // Machine Vision Inspection Reticle Brackets (4 corners)
            float rx = size * 0.14f;
            float ry = size * 0.16f;
            float rw = size * 0.72f;
            float rh = size * 0.40f;
            float blen = size * 0.06f;

            using (var bracketPen = new Pen(Color.FromArgb(160, 11, 83, 148), 2.5f))
            {
                // Top-Left
                g.DrawLine(bracketPen, rx, ry, rx + blen, ry);
                g.DrawLine(bracketPen, rx, ry, rx, ry + blen);
                // Top-Right
                g.DrawLine(bracketPen, rx + rw, ry, rx + rw - blen, ry);
                g.DrawLine(bracketPen, rx + rw, ry, rx + rw, ry + blen);
                // Bottom-Left
                g.DrawLine(bracketPen, rx, ry + rh, rx + blen, ry + rh);
                g.DrawLine(bracketPen, rx, ry + rh, rx, ry + rh - blen);
                // Bottom-Right
                g.DrawLine(bracketPen, rx + rw, ry + rh, rx + rw - blen, ry + rh);
                g.DrawLine(bracketPen, rx + rw, ry + rh, rx + rw, ry + rh - blen);
            }

            // Draw Logo in center of reticle
            float logoPaddingX = size * 0.04f;
            float logoPaddingY = size * 0.03f;
            var logoRect = new RectangleF(rx + logoPaddingX, ry + logoPaddingY, rw - 2 * logoPaddingX, rh - 2 * logoPaddingY);
            DrawLogoFitted(g, srcLogo, logoRect);

            // Bottom "VISION" Badge
            float badgeW = size * 0.64f;
            float badgeH = size * 0.16f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.64f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.05f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 11, 83, 148), Color.FromArgb(255, 4, 52, 98), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
                using (var badgeBorder = new Pen(Color.FromArgb(180, 56, 189, 248), 1.5f))
                {
                    g.DrawPath(badgeBorder, badgePath);
                }
            }

            using var font = new Font("Segoe UI", size * 0.082f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V I S I O N", font, textBrush, new RectangleF(badgeX, badgeY - 1, badgeW, badgeH), sf);

            // Outer Stroke
            using (var borderPen = new Pen(Color.FromArgb(180, 186, 218, 245), size * 0.015f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        return bmp;
    }

    private static void DrawLogoFitted(Graphics g, Bitmap logo, RectangleF targetRect)
    {
        float scale = Math.Min(targetRect.Width / logo.Width, targetRect.Height / logo.Height);
        float dw = logo.Width * scale;
        float dh = logo.Height * scale;
        float dx = targetRect.X + (targetRect.Width - dw) / 2f;
        float dy = targetRect.Y + (targetRect.Height - dh) / 2f;

        g.DrawImage(logo, dx, dy, dw, dh);
    }

    private static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ConfigureGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>
    /// Creates a 100% Windows PE (csc.exe / rc.exe / Win32 resource compiler) compliant ICO file.
    /// Uses 32bpp DIB (BITMAPINFOHEADER + BGRA + AND mask) for standard sizes (16, 24, 32, 48, 64, 128)
    /// and standard PNG compression for 256x256.
    /// </summary>
    public static void CreateStandardWindowsIco(Bitmap masterBmp, string outputIcoPath)
    {
        int[] sizes = [256, 128, 64, 48, 32, 24, 16];
        var imageEntries = new List<(int size, byte[] data)>();

        foreach (var s in sizes)
        {
            using var resized = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                ConfigureGraphics(g);
                g.DrawImage(masterBmp, 0, 0, s, s);
            }

            byte[] data;
            if (s >= 256)
            {
                // 256x256: PNG compressed format (standard Vista/Win7/10/11)
                using var ms = new MemoryStream();
                resized.Save(ms, ImageFormat.Png);
                data = ms.ToArray();
            }
            else
            {
                // Standard sizes (16, 24, 32, 48, 64, 128): Standard Win32 DIB format (BITMAPINFOHEADER)
                data = CreateDibIconData(resized);
            }

            imageEntries.Add((s, data));
        }

        using var fs = new FileStream(outputIcoPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR Header (6 bytes)
        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType = 1 (ICON)
        bw.Write((ushort)imageEntries.Count); // idCount

        int headerSize = 6;
        int dirEntrySize = 16;
        int currentOffset = headerSize + dirEntrySize * imageEntries.Count;

        // Directory Entries (16 bytes each)
        foreach (var (size, data) in imageEntries)
        {
            byte w = size >= 256 ? (byte)0 : (byte)size;
            byte h = size >= 256 ? (byte)0 : (byte)size;

            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0); // bColorCount
            bw.Write((byte)0); // bReserved
            bw.Write((ushort)1); // wPlanes
            bw.Write((ushort)32); // wBitCount
            bw.Write((uint)data.Length); // dwBytesInRes
            bw.Write((uint)currentOffset); // dwImageOffset

            currentOffset += data.Length;
        }

        // Image Data blocks
        foreach (var (_, data) in imageEntries)
        {
            bw.Write(data);
        }
    }

    private static byte[] CreateDibIconData(Bitmap bmp)
    {
        int width = bmp.Width;
        int height = bmp.Height;

        int andMaskStride = ((width + 31) / 32) * 4;
        int andMaskSize = andMaskStride * height;
        int xorSize = width * height * 4;
        int totalSize = 40 + xorSize + andMaskSize;

        using var ms = new MemoryStream(totalSize);
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 bytes)
        bw.Write((uint)40);          // biSize
        bw.Write((int)width);        // biWidth
        bw.Write((int)(height * 2)); // biHeight (XOR + AND mask combined as required by Win32 icon format)
        bw.Write((ushort)1);         // biPlanes
        bw.Write((ushort)32);        // biBitCount
        bw.Write((uint)0);           // biCompression = BI_RGB
        bw.Write((uint)(xorSize + andMaskSize)); // biSizeImage
        bw.Write((int)0);            // biXPelsPerMeter
        bw.Write((int)0);            // biYPelsPerMeter
        bw.Write((uint)0);           // biClrUsed
        bw.Write((uint)0);           // biClrImportant

        // Pixel data: bottom-up 32bpp BGRA
        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] rowBytes = new byte[width * 4];
            for (int y = height - 1; y >= 0; y--)
            {
                IntPtr rowPtr = bmpData.Scan0 + (y * bmpData.Stride);
                Marshal.Copy(rowPtr, rowBytes, 0, rowBytes.Length);
                bw.Write(rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }

        // AND mask (all 0s for 32-bit alpha icon)
        byte[] andMask = new byte[andMaskSize];
        bw.Write(andMask);

        return ms.ToArray();
    }

    public static void VerifyExeIcon()
    {
        string exePath = @"G:\NODEJS\Vision2026\VisionInspectionApp.UI\bin\Debug\net8.0-windows\VisionInspectionApp.UI.exe";
        if (File.Exists(exePath))
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon != null)
            {
                using var bmp = icon.ToBitmap();
                string outPath = @"G:\NODEJS\Vision2026\TestExtractApp\IconsOutput\extracted_exe_icon.png";
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("✅ Extracted EXE icon successfully to: " + outPath);
            }
        }
    }
}
