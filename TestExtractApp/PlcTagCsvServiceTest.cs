using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class PlcTagCsvServiceTest
{
    public static void RunTests()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("STARTING PLC TAG CSV IMPORT / EXPORT UNIT TESTS");
        Console.WriteLine("====================================================");

        int passed = 0;
        int failed = 0;

        void Assert(string testName, bool condition, string? extra = null)
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {testName} {(extra != null ? " - " + extra : "")}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {testName} {(extra != null ? " - " + extra : "")}");
                failed++;
            }
        }

        // -------------------------------------------------------------
        // TEST 1: Import Actual GX Works 3 Global Labels CSV
        // -------------------------------------------------------------
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PLC_Programs", "Mitsubishi_GXWorks3", "GlobalLabels_GXWorks3.csv");
            if (!File.Exists(filePath))
            {
                filePath = @"g:\NODEJS\Vision2026\PLC_Programs\Mitsubishi_GXWorks3\GlobalLabels_GXWorks3.csv";
            }

            Assert("Test 1.0: GlobalLabels_GXWorks3.csv exists", File.Exists(filePath), filePath);

            if (File.Exists(filePath))
            {
                string csvContent = File.ReadAllText(filePath);
                var format = PlcTagCsvService.DetectCsvFormat(csvContent);
                Assert("Test 1.1: Detect GX Works 3 Global Labels Format", format == PlcTagCsvFormat.GxWorks3GlobalLabels);

                var tags = PlcTagCsvService.ParseCsv(csvContent, "PLC_TEST", PlcTagCsvFormat.AutoDetect);
                Assert("Test 1.2: Parse Tags Count >= 20", tags.Count >= 20, $"Parsed {tags.Count} tags");

                var readyTag = tags.FirstOrDefault(t => t.Name == "Vision_Ready");
                Assert("Test 1.3: Vision_Ready parsed accurately", 
                    readyTag != null && readyTag.Address == "Y1" && readyTag.DataType == PlcDataType.Bool && readyTag.PlcId == "PLC_TEST");

                var countTag = tags.FirstOrDefault(t => t.Name == "Total_Inspected_Count");
                Assert("Test 1.4: Total_Inspected_Count DataType is Int32",
                    countTag != null && countTag.Address == "D300" && countTag.DataType == PlcDataType.Int32);

                var originXTag = tags.FirstOrDefault(t => t.Name == "Origin_X");
                Assert("Test 1.5: Origin_X DataType is Float",
                    originXTag != null && originXTag.Address == "D200" && originXTag.DataType == PlcDataType.Float);

                var speedTag = tags.FirstOrDefault(t => t.Name == "Line_Speed_Mpm");
                Assert("Test 1.6: Line_Speed_Mpm DataType is Int16",
                    speedTag != null && speedTag.Address == "D1002" && speedTag.DataType == PlcDataType.Int16);
            }
        }

        // -------------------------------------------------------------
        // TEST 2: Import Actual GX Works Device Comments CSV
        // -------------------------------------------------------------
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PLC_Programs", "Mitsubishi_GXWorks3", "DeviceComments_GXWorks.csv");
            if (!File.Exists(filePath))
            {
                filePath = @"g:\NODEJS\Vision2026\PLC_Programs\Mitsubishi_GXWorks3\DeviceComments_GXWorks.csv";
            }

            Assert("Test 2.0: DeviceComments_GXWorks.csv exists", File.Exists(filePath), filePath);

            if (File.Exists(filePath))
            {
                string csvContent = File.ReadAllText(filePath);
                var format = PlcTagCsvService.DetectCsvFormat(csvContent);
                Assert("Test 2.1: Detect GX Works Device Comments Format", format == PlcTagCsvFormat.GxWorksDeviceComments);

                var tags = PlcTagCsvService.ParseCsv(csvContent, "PLC_FX5U");
                Assert("Test 2.2: Parse Device Comments Count >= 25", tags.Count >= 25, $"Parsed {tags.Count} tags");

                var x0Tag = tags.FirstOrDefault(t => t.Address == "X0");
                Assert("Test 2.3: X0 Device Comment Name & Bool Type",
                    x0Tag != null && x0Tag.Name == "PLC_Heartbeat" && x0Tag.DataType == PlcDataType.Bool);

                var d1000Tag = tags.FirstOrDefault(t => t.Address == "D1000");
                Assert("Test 2.4: D1000 Name extraction from Comment",
                    d1000Tag != null && d1000Tag.Name == "Current_Encoder_Pulses");
            }
        }

        // -------------------------------------------------------------
        // TEST 3: Import Standard CSV Format
        // -------------------------------------------------------------
        {
            string standardCsv = 
                "Tag Name,Address,Data Type,Read Only,Description,PLC ID\n" +
                "TriggerSensor,X2,Bool,True,Optical Part Sensor,PLC_MAIN\n" +
                "RejectValve,Y20,Bool,False,High-speed Pneumatic Valve,PLC_MAIN\n" +
                "ConveyorSpeed,D1002,Int16,True,Belt Speed in m/min,PLC_MAIN\n" +
                "DefectXPos,D200,Float,True,X Position offset,PLC_MAIN\n";

            var format = PlcTagCsvService.DetectCsvFormat(standardCsv);
            Assert("Test 3.1: Detect Standard CSV Format", format == PlcTagCsvFormat.StandardCsv);

            var tags = PlcTagCsvService.ParseCsv(standardCsv, "DEFAULT_PLC");
            Assert("Test 3.2: Parse Standard CSV Count == 4", tags.Count == 4);

            var trigger = tags.First(t => t.Name == "TriggerSensor");
            Assert("Test 3.3: Standard Tag Properties Preserved",
                trigger.Address == "X2" && trigger.DataType == PlcDataType.Bool && trigger.ReadOnly && trigger.PlcId == "PLC_MAIN");
        }

        // -------------------------------------------------------------
        // TEST 4: Export to all 3 Formats & Round-Trip Validation
        // -------------------------------------------------------------
        {
            var originalTags = new List<PlcTag>
            {
                new() { PlcId = "PLC1", Name = "Vision_Ready", Address = "Y1", DataType = PlcDataType.Bool, Description = "Ready signal" },
                new() { PlcId = "PLC1", Name = "Line_Speed", Address = "D1002", DataType = PlcDataType.Int16, Description = "Conveyor speed" },
                new() { PlcId = "PLC1", Name = "Part_Score", Address = "D200", DataType = PlcDataType.Float, Description = "Inspection score" },
                new() { PlcId = "PLC1", Name = "Total_Count", Address = "D300", DataType = PlcDataType.Int32, Description = "Total count" }
            };

            // 1. Export Standard CSV
            string stdCsv = PlcTagCsvService.ExportToStandardCsv(originalTags);
            var reParsedStd = PlcTagCsvService.ParseCsv(stdCsv, "PLC1");
            Assert("Test 4.1: Standard CSV Round-trip Count == 4", reParsedStd.Count == 4);
            Assert("Test 4.2: Standard CSV Round-trip Data Preserved", 
                reParsedStd.Any(t => t.Name == "Vision_Ready" && t.Address == "Y1" && t.DataType == PlcDataType.Bool) &&
                reParsedStd.Any(t => t.Name == "Part_Score" && t.DataType == PlcDataType.Float));

            // 2. Export GX Works 3 Global Labels CSV
            string gxGlobalCsv = PlcTagCsvService.ExportToGxWorksGlobalLabelsCsv(originalTags);
            var reParsedGx = PlcTagCsvService.ParseCsv(gxGlobalCsv, "PLC1");
            Assert("Test 4.3: GX Works 3 Global Labels Round-trip Count == 4", reParsedGx.Count == 4);
            Assert("Test 4.4: GX Works 3 Global Labels DataType Preserved",
                reParsedGx.Any(t => t.Name == "Part_Score" && t.DataType == PlcDataType.Float) &&
                reParsedGx.Any(t => t.Name == "Total_Count" && t.DataType == PlcDataType.Int32));

            // 3. Export GX Works Device Comments CSV
            string gxDevCsv = PlcTagCsvService.ExportToGxWorksDeviceCommentsCsv(originalTags);
            var reParsedDev = PlcTagCsvService.ParseCsv(gxDevCsv, "PLC1");
            Assert("Test 4.5: GX Works Device Comments Round-trip Count == 4", reParsedDev.Count == 4);
            Assert("Test 4.6: GX Works Device Comments Address Preserved",
                reParsedDev.Any(t => t.Address == "Y1") && reParsedDev.Any(t => t.Address == "D1002"));
        }

        // -------------------------------------------------------------
        // TEST 5: Robust RFC 4180 Quotes & Semicolon Handling
        // -------------------------------------------------------------
        {
            string complexLine = "\"VAR_GLOBAL\",\"Special,Tag\",\"Bit\",\"\",\"Y10\",\"\",\"Comment with \"\"nested quotes\"\" and, commas\"";
            var tokens = PlcTagCsvService.ParseCsvLine(complexLine);

            Assert("Test 5.1: Parse RFC 4180 Quoted Line Tokens == 7", tokens.Count == 7);
            Assert("Test 5.2: Preserved commas inside quoted tokens", tokens[1] == "Special,Tag");
            Assert("Test 5.3: Handled nested escaped quotes", tokens[6].Contains("nested quotes") && tokens[6].Contains("commas"));

            string semicolonLine = "Device;Comment\nX10;Trigger \"Start\" Sensor; with details";
            var semiTags = PlcTagCsvService.ParseCsv(semicolonLine, "PLC_SEMI");
            Assert("Test 5.4: Semicolon delimiter parsed successfully", semiTags.Count == 1 && semiTags[0].Address == "X10");
        }

        Console.WriteLine("====================================================");
        Console.WriteLine($"PLC TAG CSV TESTS SUMMARY: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("====================================================");

        if (failed > 0)
        {
            throw new Exception($"PlcTagCsvServiceTest failed with {failed} failure(s)!");
        }
    }
}
