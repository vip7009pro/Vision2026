using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0].Equals("android-camera", StringComparison.OrdinalIgnoreCase) || args[0].Equals("android", StringComparison.OrdinalIgnoreCase) || args[0].Equals("s24", StringComparison.OrdinalIgnoreCase)))
        {
            AndroidCameraProModeTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("column", StringComparison.OrdinalIgnoreCase) || args[0].Equals("column-resize", StringComparison.OrdinalIgnoreCase) || args[0].Equals("editor-column", StringComparison.OrdinalIgnoreCase)))
        {
            ToolEditorColumnResizeTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("plc-queue", StringComparison.OrdinalIgnoreCase) || args[0].Equals("continuous-queue", StringComparison.OrdinalIgnoreCase) || args[0].Equals("plc-continuous", StringComparison.OrdinalIgnoreCase)))
        {
            PlcContinuousQueueTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("reset-queue", StringComparison.OrdinalIgnoreCase) || args[0].Equals("hmi-queue", StringComparison.OrdinalIgnoreCase)))
        {
            PlcResetQueueAndHmiTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("plc-isolation", StringComparison.OrdinalIgnoreCase) || args[0].Equals("plc-conflict", StringComparison.OrdinalIgnoreCase)))
        {
            PlcConflictAndHmiIsolationTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("sc", StringComparison.OrdinalIgnoreCase) || args[0].Equals("sc-roi", StringComparison.OrdinalIgnoreCase)))
        {
            SurfaceCompareAndCaliperRoiTests.RunTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("sc-repro", StringComparison.OrdinalIgnoreCase))
        {
            SurfaceCompareAndCaliperRoiTests.Test_ReproduceUserJobBlackTemplate();
            return;
        }

        if (args.Length > 0 && args[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
        {
            SystemConfigBackupAndOqcDbMatchTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("mvp2", StringComparison.OrdinalIgnoreCase))
        {
            MvpShapeMatch2PreprocessTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("contour", StringComparison.OrdinalIgnoreCase))
        {
            ContourCompareTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("config", StringComparison.OrdinalIgnoreCase) || args[0].Equals("config-persist", StringComparison.OrdinalIgnoreCase)))
        {
            ReleaseConfigPersistenceTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("calib", StringComparison.OrdinalIgnoreCase))
        {
            ToolCalibFactorTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            PdfSourceTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("newjob", StringComparison.OrdinalIgnoreCase))
        {
            NewJobDefaultToolsTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("roi", StringComparison.OrdinalIgnoreCase))
        {
            PerformanceOptimizationTests.RunRoiEditRegressionTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("origin", StringComparison.OrdinalIgnoreCase))
        {
            OriginTemplateJobTest.RunTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("productassign", StringComparison.OrdinalIgnoreCase))
        {
            ProductAssignAndCodeSyncTest.RunTests();
            RemoteServerAndJobManagerTests.RunTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("oqc", StringComparison.OrdinalIgnoreCase) || args[0].Equals("crosshair", StringComparison.OrdinalIgnoreCase)))
        {
            OqcLiveViewOnJobLoadTests.RunTests();
            CrosshairOverlayTests.RunTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("hmi", StringComparison.OrdinalIgnoreCase) || args[0].Equals("hmi-test", StringComparison.OrdinalIgnoreCase)))
        {
            HmiDesignerPersistenceTests.RunAllTests();
            HmiStateAndLifecycleTests.RunAllTests();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("signal", StringComparison.OrdinalIgnoreCase) || args[0].Equals("plc-signal", StringComparison.OrdinalIgnoreCase)))
        {
            PlcSignalTypeTests.RunAllTestsAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Length > 0 && (args[0].Equals("handshake", StringComparison.OrdinalIgnoreCase) || args[0].Equals("plc-handshake", StringComparison.OrdinalIgnoreCase)))
        {
            PlcHandshakePersistenceTests.RunAllTests();
            return;
        }

        HikApiTest.PrintApi();
        CameraTest.TestCameraParametersJobSerialization();
        CameraTest.TestNativeMatPoolAndMetadata();
        CameraTest.TestPlcMotionSyncService();
        CameraTest.TestRollDefectManagerAndShiftRegister();
        CameraTest.TestPhase5IndustrialHandshakeAndSoakTest();
        CameraTest.TestIndustrialUIAndQueueVisualization();
        CameraTest.TestDirectAddressSupport();
        CameraTest.TestZeroAllocationLiveViewAndMemoryOptimization();
        CameraTest.TestContinuousEngineHandshakeBypass();
        CameraTest.TestSystemMonitorAndNonBlockingRender();
        CameraTest.TestInspectionLogAndSpcEngine();
        CameraTest.TestFlowCanvasNodeRenameAndDownstreamReferences();
        CaliperAndLineTest.RunTests();
        ColorDiffTest.RunTests();
        RecentJobsAndCalibrationTest.RunTests();
        IconGenerator.GenerateAppIcons();
        ContinuousPipelineTest.RunTestsAsync().GetAwaiter().GetResult();
        PlcTagCsvServiceTest.RunTests();
        PlcTests.RunAllTestsAsync().GetAwaiter().GetResult();
        ManualInspectionTest.RunTests();
        LightingControllerTests.RunAllTests();
        LightingServerClientTests.RunTests();
        LightingPatternTests.RunTests();
        PreprocessRoiMaskingTest.RunTests();
        OriginTemplateJobTest.RunTests();
        ProductAssignAndCodeSyncTest.RunTests();
        PlcBridgeTest.RunTestsAsync().GetAwaiter().GetResult();
        RemoteServerAndJobManagerTests.RunTests();
        PreviewQualityTests.RunTests();
        NewJobAndBlobSpecTests.RunTests();
        UrlImageSourceAndRecentJobTests.RunTests();
        BlobCountingModeTests.RunTests();
        OqcLiveViewOnJobLoadTests.RunTests();
        CrosshairOverlayTests.RunTests();
        PreprocessAndImageOutputTests.RunTests();
        SurfaceCompareAndCaliperRoiTests.RunTests();
        OcrDetectorTests.RunTests();
        OtaUpdateServiceTests.RunAllTests();
        OtaPublisherServiceTests.RunAllTests();
        DocumentationSystemTests.RunAllTests();
        ChessboardRobustnessTests.RunAllTests();
        ChessboardCalibrationMismatchTests.RunAllTests();
        LicenseSystemTests.RunAllTests();
        DotnetRuntimeTests.RunAllTests();
        NewJobDefaultToolsTests.RunAllTests();
        UndistortPipelineTests.RunAllTests();
        UndistortResolutionTests.RunAll();
        PerformanceOptimizationTests.RunAllTests();
        ContinuousFlowRegressionTests.RunAllTests();
        ToolEditorAndOqcUxTests.RunAllTests();
        TimingAccountingTests.RunAllTests();
        PdfSourceTests.RunAllTests();
        SystemConfigBackupAndOqcDbMatchTests.RunAllTests();
        ToolCalibFactorTests.RunAllTests();
        ReleaseConfigPersistenceTests.RunAllTests();
        MvpShapeMatch2PreprocessTests.RunAllTests();
        ContourCompareTests.RunAllTests();
        HmiDesignerPersistenceTests.RunAllTests();
        HmiStateAndLifecycleTests.RunAllTests();
        PlcSignalTypeTests.RunAllTestsAsync().GetAwaiter().GetResult();
        PlcHandshakePersistenceTests.RunAllTests();
        ToolEditorColumnResizeTests.RunAllTests();
    }
}
