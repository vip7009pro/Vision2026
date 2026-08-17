using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class VariableInjectionTest
{
    public static void RunTests()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("STARTING VARIABLE INJECTION & TEXT TEMPLATE TESTS");
        Console.WriteLine("====================================================");

        var config = new VisionConfig
        {
            PixelsPerMm = 20.0, // 20 px/mm => 1 px = 0.05 mm
            ProductCode = "PROD-2026",
            ProductName = "Test Camera Unit"
        };

        var result = new InspectionResult
        {
            Pass = true
        };
        result.Timings.TotalMs = 43;

        // 1. Origin
        result.Origin = new PointMatchResult(
            Name: "Origin",
            Position: new Point2d(150.256, 200.789),
            MatchRect: new Rect(100, 100, 100, 100),
            Score: 0.985,
            Threshold: 0.8,
            Pass: true,
            AngleDeg: 45.678
        );

        // 2. Points
        result.Points.Add(new PointMatchResult("Point1", new Point2d(100.0, 150.0), new Rect(50, 50, 80, 80), 0.95, 0.8, true, 12.3));
        result.Points.Add(new PointMatchResult("P2", new Point2d(200.0, 250.0), new Rect(50, 50, 80, 80), 0.92, 0.8, true, -5.4));

        // 3. Lines
        result.Lines.Add(new LineDetectResult("Line1", new Point2d(10, 20), new Point2d(300, 100), 350.5, true));

        // 4. Distances
        result.Distances.Add(new DistanceCheckResult("Dist1", "P1", "P2", 25.4, 25.0, 0.5, 0.5, true));

        // 5. Circles
        result.CircleFinders.Add(new CircleFinderResult("Circle1", true, new Point2d(100, 100), 50.0, 0.99));

        // 6. DB
        result.DbResults.Add(new DbResult
        {
            NodeName = "DB1",
            Success = true,
            Value = "BATCH_OK_999",
            Text = "BATCH_OK_999",
            RowCount = 5,
            ColumnCount = 2
        });

        // 7. PLC
        result.PlcReads.Add(new PlcReadResult("PLC1", "PLC_MAIN", "D100", 888.88, true));

        // Build Variable Map
        var vars = ConditionEvaluator.BuildVariableMap(result, config);

        // Run Test Cases
        var testCases = new (string Template, string ExpectedSnippet, string Description)[]
        {
            // The exact issue user reported:
            ("{Origin1.Angle}", "45.678", "User reported case: {Origin1.Angle}"),
            ("{Origin.Angle}", "45.678", "Standard alias: {Origin.Angle}"),
            ("{Origin1.Angle:F1}", "45.7", "Formatted: {Origin1.Angle:F1}"),
            ("{Origin1.Score:P1}", "98.5", "Percentage format: {Origin1.Score:P1}"),
            ("{Origin1.X}", "150.256", "Origin X px"),
            ("{Origin1.X_mm:F2}", "7.51", "Origin X in mm (150.256 / 20 = 7.5128)"),
            ("{Origin1.Status}", "OK", "Origin Status string"),

            // Points
            ("{Point1.Angle}", "12.3", "Point1 Angle"),
            ("{P1.Angle}", "12.3", "Point1 alias P1.Angle"),
            ("{P2.Angle}", "-5.4", "Point2 alias P2.Angle"),
            ("{Point1.X_mm:F1}", "5.0", "Point1 X mm (100 / 20 = 5.0)"),

            // Lines
            ("{Line1.Length}", "350.5", "Line1 Length"),

            // Distances
            ("{Dist1.Value}", "25.4", "Dist1 Value"),
            ("{Dist1.Diff:F2}", "0.40", "Dist1 Diff (25.4 - 25.0)"),
            ("{Dist1.Status}", "OK", "Dist1 Status"),

            // Circles
            ("{Circle1.Diameter}", "100", "Circle1 Diameter"),
            ("{Circle1.Radius}", "50", "Circle1 Radius"),
            ("{CIR1.CenterX}", "100", "Circle1 alias CIR1.CenterX"),

            // DB
            ("{DB1.Value}", "BATCH_OK_999", "DB1 Value text"),
            ("{DB1.RowCount}", "5", "DB1 RowCount"),

            // PLC
            ("{PLC1.Value}", "888.88", "PLC1 Value"),
            ("{PLC1.TagName}", "D100", "PLC1 TagName"),

            // Global
            ("{Status}", "PASS", "Global Status"),
            ("{ProductCode}", "PROD-2026", "Global ProductCode"),
            ("{TotalMs}", "43", "Global TotalMs")
        };

        int passed = 0;
        int failed = 0;

        foreach (var tc in testCases)
        {
            string output = ConditionEvaluator.EvaluateTextTemplate(tc.Template, vars);
            bool ok = output.Contains(tc.ExpectedSnippet, StringComparison.OrdinalIgnoreCase);

            if (ok)
            {
                Console.WriteLine($"[PASS] {tc.Description} -> Template: \"{tc.Template}\" => \"{output}\" (Matched: \"{tc.ExpectedSnippet}\")");
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {tc.Description} -> Template: \"{tc.Template}\" => \"{output}\" (Expected to contain: \"{tc.ExpectedSnippet}\")");
                Console.ResetColor();
                failed++;
            }
        }

        // Logic Expressions Test
        Console.WriteLine("\n--- Testing Logic Expressions ---");
        var logicTests = new (string Expr, bool Expected, string Desc)[]
        {
            ("Origin1.Angle > 40 && Origin1.Pass == true", true, "Origin1.Angle > 40 && Origin1.Pass == true"),
            ("Origin1.Score > 0.95 && Point1.Angle == 12.3", true, "Origin1.Score > 0.95 && Point1.Angle == 12.3"),
            ("Dist1.Value >= 25.0 && Circle1.Diameter == 100", true, "Dist1.Value >= 25.0 && Circle1.Diameter == 100"),
            ("DB1.RowCount == 5 && PLC1.TagName == 'D100'", true, "DB1.RowCount == 5 && PLC1.TagName == 'D100'"),
            ("Origin1.Angle < 30", false, "Origin1.Angle < 30 (should be false)")
        };

        foreach (var lt in logicTests)
        {
            try
            {
                bool eval = ConditionEvaluator.Evaluate(lt.Expr, vars);
                if (eval == lt.Expected)
                {
                    Console.WriteLine($"[PASS] Expression: \"{lt.Expr}\" => {eval}");
                    passed++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAIL] Expression: \"{lt.Expr}\" => {eval} (Expected: {lt.Expected})");
                    Console.ResetColor();
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL-EXCEPTION] Expression: \"{lt.Expr}\" => Error: {ex.Message}");
                Console.ResetColor();
                failed++;
            }
        }

        Console.WriteLine("\n====================================================");
        Console.WriteLine($"TEST SUMMARY: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("====================================================");

        if (failed > 0)
        {
            throw new Exception($"Test failed with {failed} failures.");
        }
    }
}
