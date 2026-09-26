using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
        {
            SystemConfigBackupAndOqcDbMatchTests.RunAllTests();
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
    }
}
