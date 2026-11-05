file_path = "TestExtractApp/NewJobAndBlobSpecTests.cs"
with open(file_path, "rb") as f:
    content = f.read()

# Thay thế logic trong hàm TestJob223Debug để in thử kết quả với logic mới:
old_inspect = """        // Test with HUGE WxL
        Console.WriteLine("DEBUG 223: --- Testing BLD1 with HUGE WxL (100x100 mm) ---");
        var bld1 = config.BlobDetections.FirstOrDefault(x => x.Name == "BLD1");
        if (bld1 != null)
        {
            bld1.MaxBlobWidth = 100.0;
            bld1.MaxBlobLength = 100.0;
            var resHuge = service.Inspect(img, config);
            var bdHuge = resHuge.BlobDetections.FirstOrDefault(x => x.Name == "BLD1");
            Console.WriteLine($"DEBUG 223: BLD1 with WxL=100x100: Count={bdHuge?.Count}");
            for (int i = 0; i < Math.Min(bdHuge?.Blobs.Count ?? 0, 15); i++)
            {
                var b = bdHuge.Blobs[i];
                double bwMm = b.BoundingBox.Width / config.PixelsPerMm;
                double bhMm = b.BoundingBox.Height / config.PixelsPerMm;
                Console.WriteLine($"   Blob[{i}]: AreaPx={b.Area}, WxH_Px={b.BoundingBox.Width}x{b.BoundingBox.Height}, WxH_Mm={bwMm:F2}x{bhMm:F2} mm");
            }
        }

        // Test with SMALL WxL
        Console.WriteLine("DEBUG 223: --- Testing BLD1 with SMALL WxL (0.1x0.1 mm) ---");
        if (bld1 != null)
        {
            bld1.MaxBlobWidth = 0.1;
            bld1.MaxBlobLength = 0.1;
            var resSmall = service.Inspect(img, config);
            var bdSmall = resSmall.BlobDetections.FirstOrDefault(x => x.Name == "BLD1");
            Console.WriteLine($"DEBUG 223: BLD1 with WxL=0.1x0.1: Count={bdSmall?.Count}");
        }"""

new_inspect = """        Console.WriteLine("DEBUG 223: Simulation completed.");"""

content = content.replace(old_inspect, new_inspect)
with open(file_path, "wb") as f:
    f.write(content)
print("Updated test file!")
