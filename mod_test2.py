file_path = "TestExtractApp/NewJobAndBlobSpecTests.cs"
with open(file_path, "rb") as f:
    content = f.read()

# Chèn đoạn code test logic withinTolerance
test_code = """
        // Test logic: Defect blob detection
        Console.WriteLine("DEBUG 223: --- Testing Defect Blob Logic (Blob caught ONLY if exceeds WxL) ---");
        // With spec 3x6 mm:
        // Blobs with W <= 3 && H <= 6 are acceptable (ignored).
        // Blobs with W > 3 || H > 6 are defects (caught).
"""

content = content.replace('Console.WriteLine("DEBUG 223: Simulation completed.");', test_code)
with open(file_path, "wb") as f:
    f.write(content)
print("Updated NewJobAndBlobSpecTests.cs!")
