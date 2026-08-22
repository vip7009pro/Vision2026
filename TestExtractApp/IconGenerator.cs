using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

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

        // Render Design 1: Ultra-Clean Vision Reticle Light (Windows 11 Light Squircle)
        using var bmpReticleLight = RenderDesignReticleLight(srcLogo, 512);
        bmpReticleLight.Save(Path.Combine(outputDir, "design_reticle_light_512.png"), ImageFormat.Png);

        // Render Design 2: High-Tech Industrial Dark Squircle
        using var bmpTechDark = RenderDesignTechDark(srcLogo, 512);
        bmpTechDark.Save(Path.Combine(outputDir, "design_tech_dark_512.png"), ImageFormat.Png);

        // Render Design 3: Modern Precision Gradient Squircle
        using var bmpPrecision = RenderDesignPrecision(srcLogo, 512);
        bmpPrecision.Save(Path.Combine(outputDir, "design_precision_512.png"), ImageFormat.Png);

        // Save the chosen premium icon (bmpReticleLight / bmpPrecision) as the primary multi-resolution ICO for the App
        CreateMultiResolutionIco(bmpReticleLight, targetIcoPath);
        CreateMultiResolutionIco(bmpTechDark, Path.Combine(outputDir, "cms-vina-vision-dark.ico"));
        CreateMultiResolutionIco(bmpPrecision, Path.Combine(outputDir, "cms-vina-vision-precision.ico"));

        Console.WriteLine("Icon designs rendered and ICO saved to " + targetIcoPath);
    }

    private static Bitmap RenderDesignPrecision(Bitmap srcLogo, int size)
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
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 236, 245, 254), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // Subtle Machine Vision Optical Reticle Circle
            float cx = size * 0.5f;
            float cy = size * 0.40f;
            using (var penRing = new Pen(Color.FromArgb(30, 11, 83, 148), 1.5f))
            {
                g.DrawEllipse(penRing, cx - size * 0.34f, cy - size * 0.34f, size * 0.68f, size * 0.68f);
                g.DrawLine(penRing, cx - size * 0.36f, cy, cx - size * 0.28f, cy);
                g.DrawLine(penRing, cx + size * 0.28f, cy, cx + size * 0.36f, cy);
                g.DrawLine(penRing, cx, cy - size * 0.36f, cx, cy - size * 0.28f);
                g.DrawLine(penRing, cx, cy + size * 0.28f, cx, cy + size * 0.36f);
            }

            // Draw Logo in center
            float logoW = size * 0.76f;
            float logoH = size * 0.36f;
            float logoX = (size - logoW) / 2f;
            float logoY = size * 0.17f;
            DrawLogoFitted(g, srcLogo, new RectangleF(logoX, logoY, logoW, logoH));

            // Bottom "VISION" Badge with subtle cyan highlight
            float badgeW = size * 0.68f;
            float badgeH = size * 0.17f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.63f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.085f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 11, 83, 148), Color.FromArgb(255, 6, 52, 98), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
                using (var badgeBorder = new Pen(Color.FromArgb(160, 56, 189, 248), 1.5f))
                {
                    g.DrawPath(badgeBorder, badgePath);
                }
            }

            using var font = new Font("Segoe UI", size * 0.086f, FontStyle.Bold, GraphicsUnit.Pixel);
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

    private static Bitmap RenderDesignTechDark(Bitmap srcLogo, int size)
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
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 1, margin + size * 0.025f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Dark Blue / Slate Gradient
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 12, 22, 45), Color.FromArgb(255, 20, 36, 68), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // White / frosted glass plate behind the logo so the original blue logo pops brilliantly
            float plateW = size * 0.80f;
            float plateH = size * 0.38f;
            float plateX = (size - plateW) / 2f;
            float plateY = size * 0.16f;
            var plateRect = new RectangleF(plateX, plateY, plateW, plateH);
            using (var platePath = GetRoundedRectPath(plateRect, size * 0.07f))
            {
                using (var plateBrush = new LinearGradientBrush(plateRect, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 240, 246, 252), 90f))
                {
                    g.FillPath(plateBrush, platePath);
                }
                using (var plateBorder = new Pen(Color.FromArgb(140, 56, 189, 248), 1.8f))
                {
                    g.DrawPath(plateBorder, platePath);
                }
            }

            // Draw Logo inside plate
            float padX = size * 0.04f;
            float padY = size * 0.03f;
            DrawLogoFitted(g, srcLogo, new RectangleF(plateX + padX, plateY + padY, plateW - 2 * padX, plateH - 2 * padY));

            // Bottom "VISION" Pill / Badge
            float badgeW = size * 0.72f;
            float badgeH = size * 0.18f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.62f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.06f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 0, 114, 206), Color.FromArgb(255, 0, 75, 150), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
                using (var badgeBorder = new Pen(Color.FromArgb(220, 56, 189, 248), 1.8f))
                {
                    g.DrawPath(badgeBorder, badgePath);
                }
            }

            // Draw Text "VISION"
            using var font = new Font("Segoe UI", size * 0.092f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V I S I O N", font, textBrush, new RectangleF(badgeX, badgeY - 1, badgeW, badgeH), sf);

            // Glowing Outer Border for Squircle
            using (var borderPen = new Pen(Color.FromArgb(180, 56, 189, 248), size * 0.015f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        return bmp;
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

    private static Bitmap RenderDesignReticleDark(Bitmap srcLogo, int size)
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
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 1, margin + size * 0.025f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Dark Blue / Midnight gradient
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 10, 20, 38), Color.FromArgb(255, 16, 32, 58), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // White Inner Card with subtle rounded corners for CMS Logo
            float cardW = size * 0.80f;
            float cardH = size * 0.38f;
            float cardX = (size - cardW) / 2f;
            float cardY = size * 0.15f;
            var cardRect = new RectangleF(cardX, cardY, cardW, cardH);
            using (var cardPath = GetRoundedRectPath(cardRect, size * 0.06f))
            {
                using (var cardBrush = new LinearGradientBrush(cardRect, Color.White, Color.FromArgb(242, 247, 252), 90f))
                {
                    g.FillPath(cardBrush, cardPath);
                }
                using (var cardBorder = new Pen(Color.FromArgb(140, 56, 189, 248), 1.8f))
                {
                    g.DrawPath(cardBorder, cardPath);
                }
            }

            // Draw Logo inside card
            float padX = size * 0.04f;
            float padY = size * 0.03f;
            DrawLogoFitted(g, srcLogo, new RectangleF(cardX + padX, cardY + padY, cardW - 2 * padX, cardH - 2 * padY));

            // Bottom "VISION" Tech Badge
            float badgeW = size * 0.72f;
            float badgeH = size * 0.18f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.62f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.05f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 0, 114, 206), Color.FromArgb(255, 0, 75, 150), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
                using (var badgeBorder = new Pen(Color.FromArgb(220, 56, 189, 248), 1.8f))
                {
                    g.DrawPath(badgeBorder, badgePath);
                }
            }

            using var font = new Font("Segoe UI", size * 0.092f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V I S I O N", font, textBrush, new RectangleF(badgeX, badgeY - 1, badgeW, badgeH), sf);

            // Outer Neon Border
            using (var borderPen = new Pen(Color.FromArgb(180, 56, 189, 248), size * 0.015f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        return bmp;
    }

    private static Bitmap RenderDesignDark(Bitmap srcLogo, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        ConfigureGraphics(g);

        // Outer rounded squircle
        float margin = size * 0.04f;
        float cornerRadius = size * 0.22f;
        var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

        using (var path = GetRoundedRectPath(rect, cornerRadius))
        {
            // Drop shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 2, margin + size * 0.02f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Dark Blue / Slate Gradient background
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 12, 22, 45), Color.FromArgb(255, 20, 36, 68), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // High-tech subtle grid / aperture background accent
            using (var penGrid = new Pen(Color.FromArgb(25, 56, 189, 248), 1.5f))
            {
                float cx = size * 0.5f;
                float cy = size * 0.42f;
                g.DrawEllipse(penGrid, cx - size * 0.32f, cy - size * 0.32f, size * 0.64f, size * 0.64f);
                g.DrawEllipse(penGrid, cx - size * 0.24f, cy - size * 0.24f, size * 0.48f, size * 0.48f);
                // Reticle crosshair marks
                g.DrawLine(penGrid, cx - size * 0.36f, cy, cx - size * 0.26f, cy);
                g.DrawLine(penGrid, cx + size * 0.26f, cy, cx + size * 0.36f, cy);
                g.DrawLine(penGrid, cx, cy - size * 0.36f, cx, cy - size * 0.26f);
                g.DrawLine(penGrid, cx, cy + size * 0.26f, cx, cy + size * 0.36f);
            }

            // White / frosted glass plate behind the logo so the original blue logo pops brilliantly
            float plateW = size * 0.76f;
            float plateH = size * 0.36f;
            float plateX = (size - plateW) / 2f;
            float plateY = size * 0.18f;
            var plateRect = new RectangleF(plateX, plateY, plateW, plateH);
            using (var platePath = GetRoundedRectPath(plateRect, size * 0.08f))
            {
                using (var plateBrush = new LinearGradientBrush(plateRect, Color.FromArgb(250, 255, 255, 255), Color.FromArgb(235, 243, 250), 90f))
                {
                    g.FillPath(plateBrush, platePath);
                }
                using (var plateBorder = new Pen(Color.FromArgb(120, 56, 189, 248), 2f))
                {
                    g.DrawPath(plateBorder, platePath);
                }
            }

            // Draw Logo inside plate
            float logoPadding = size * 0.04f;
            var logoRect = new RectangleF(plateX + logoPadding, plateY + logoPadding * 0.6f, plateW - 2 * logoPadding, plateH - 1.2f * logoPadding);
            DrawLogoFitted(g, srcLogo, logoRect);

            // Bottom "VISION" Pill / Badge
            float badgeW = size * 0.72f;
            float badgeH = size * 0.18f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.62f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.06f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 0, 114, 206), Color.FromArgb(255, 0, 80, 160), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
                using (var badgeBorder = new Pen(Color.FromArgb(200, 56, 189, 248), 2f))
                {
                    g.DrawPath(badgeBorder, badgePath);
                }
            }

            // Draw Text "VISION"
            using var font = new Font("Segoe UI", size * 0.095f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V I S I O N", font, textBrush, new RectangleF(badgeX, badgeY - 1, badgeW, badgeH), sf);

            // Glowing Outer Border for Squircle
            using (var borderPen = new Pen(Color.FromArgb(180, 56, 189, 248), size * 0.015f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        return bmp;
    }

    private static Bitmap RenderDesignLight(Bitmap srcLogo, int size)
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
            using (var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 2, margin + size * 0.025f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Clean crisp white-to-pale-blue gradient
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 232, 242, 252), 90f))
            {
                g.FillPath(bgBrush, path);
            }

            // Subtle optical target ring in background
            using (var penRing = new Pen(Color.FromArgb(35, 11, 83, 148), 1.5f))
            {
                float cx = size * 0.5f;
                float cy = size * 0.42f;
                g.DrawEllipse(penRing, cx - size * 0.32f, cy - size * 0.32f, size * 0.64f, size * 0.64f);
                g.DrawLine(penRing, cx - size * 0.34f, cy, cx - size * 0.25f, cy);
                g.DrawLine(penRing, cx + size * 0.25f, cy, cx + size * 0.34f, cy);
            }

            // Logo centered in upper-middle area
            float logoW = size * 0.74f;
            float logoH = size * 0.34f;
            float logoX = (size - logoW) / 2f;
            float logoY = size * 0.19f;
            DrawLogoFitted(g, srcLogo, new RectangleF(logoX, logoY, logoW, logoH));

            // Bottom "VISION" Badge (Deep Blue pill)
            float badgeW = size * 0.68f;
            float badgeH = size * 0.17f;
            float badgeX = (size - badgeW) / 2f;
            float badgeY = size * 0.63f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using (var badgePath = GetRoundedRectPath(badgeRect, size * 0.085f))
            {
                using (var badgeBrush = new LinearGradientBrush(badgeRect, Color.FromArgb(255, 11, 83, 148), Color.FromArgb(255, 5, 50, 95), 90f))
                {
                    g.FillPath(badgeBrush, badgePath);
                }
            }

            using var font = new Font("Segoe UI", size * 0.088f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("V I S I O N", font, textBrush, new RectangleF(badgeX, badgeY - 1, badgeW, badgeH), sf);

            // Subtle border
            using (var borderPen = new Pen(Color.FromArgb(160, 180, 210, 235), size * 0.015f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        return bmp;
    }

    private static Bitmap RenderDesignOptic(Bitmap srcLogo, int size)
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
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                var shadowRect = new RectangleF(margin + 2, margin + size * 0.025f, rect.Width, rect.Height);
                using var shadowPath = GetRoundedRectPath(shadowRect, cornerRadius);
                g.FillPath(shadowBrush, shadowPath);
            }

            // Dark Slate-Navy gradient
            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(255, 15, 23, 42), Color.FromArgb(255, 30, 41, 59), 135f))
            {
                g.FillPath(bgBrush, path);
            }

            // Top camera sensor / optic aperture element
            float cx = size * 0.5f;
            float cy = size * 0.44f;
            using (var opticPen = new Pen(Color.FromArgb(40, 56, 189, 248), 2f))
            {
                g.DrawEllipse(opticPen, cx - size * 0.38f, cy - size * 0.38f, size * 0.76f, size * 0.76f);
            }

            // White Card for CMS VINA Logo
            float cardW = size * 0.78f;
            float cardH = size * 0.36f;
            float cardX = (size - cardW) / 2f;
            float cardY = size * 0.16f;
            var cardRect = new RectangleF(cardX, cardY, cardW, cardH);
            using (var cardPath = GetRoundedRectPath(cardRect, size * 0.07f))
            {
                using (var cardBrush = new LinearGradientBrush(cardRect, Color.White, Color.FromArgb(240, 246, 252), 90f))
                {
                    g.FillPath(cardBrush, cardPath);
                }
                using (var cardBorder = new Pen(Color.FromArgb(180, 14, 165, 233), 1.8f))
                {
                    g.DrawPath(cardBorder, cardPath);
                }
            }

            DrawLogoFitted(g, srcLogo, new RectangleF(cardX + size * 0.03f, cardY + size * 0.02f, cardW - size * 0.06f, cardH - size * 0.04f));

            // Bottom "VISION SYSTEM" Tech Strip
            float textY = size * 0.65f;
            using var fontVision = new Font("Segoe UI", size * 0.11f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fontSub = new Font("Segoe UI", size * 0.045f, FontStyle.Bold, GraphicsUnit.Pixel);

            using var glowBrush = new SolidBrush(Color.FromArgb(255, 56, 189, 248));
            using var whiteBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.DrawString("VISION", fontVision, glowBrush, new RectangleF(0, textY, size, size * 0.14f), sf);
            g.DrawString("I N S P E C T I O N   S Y S T E M", fontSub, new SolidBrush(Color.FromArgb(180, 203, 213, 225)), new RectangleF(0, textY + size * 0.13f, size, size * 0.06f), sf);

            // Outer Neon Border
            using (var borderPen = new Pen(Color.FromArgb(200, 14, 165, 233), size * 0.015f))
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

    public static void CreateMultiResolutionIco(Bitmap masterBmp, string outputIcoPath)
    {
        int[] sizes = [256, 128, 64, 48, 32, 16];
        var pngStreams = new List<byte[]>();

        foreach (var s in sizes)
        {
            using var resized = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                ConfigureGraphics(g);
                g.DrawImage(masterBmp, 0, 0, s, s);
            }

            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            pngStreams.Add(ms.ToArray());
        }

        using var fs = new FileStream(outputIcoPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR Header
        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType = 1 (ICON)
        bw.Write((ushort)sizes.Length); // idCount

        int headerSize = 6;
        int dirEntrySize = 16;
        int currentOffset = headerSize + dirEntrySize * sizes.Length;

        // Directory Entries
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            byte w = s >= 256 ? (byte)0 : (byte)s;
            byte h = s >= 256 ? (byte)0 : (byte)s;

            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0); // bColorCount
            bw.Write((byte)0); // bReserved
            bw.Write((ushort)1); // wPlanes
            bw.Write((ushort)32); // wBitCount
            bw.Write((uint)pngStreams[i].Length); // dwBytesInRes
            bw.Write((uint)currentOffset); // dwImageOffset

            currentOffset += pngStreams[i].Length;
        }

        // Image Data (PNG blocks)
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngStreams[i]);
        }

        Console.WriteLine($"Generated ICO with {sizes.Length} resolutions at: {outputIcoPath}");
    }
}
