using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
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
    }
}
