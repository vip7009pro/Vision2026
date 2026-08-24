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
        CaliperAndLineTest.RunTests();
        ColorDiffTest.RunTests();
        RecentJobsAndCalibrationTest.RunTests();
        IconGenerator.GenerateAppIcons();
        IconGenerator.VerifyExeIcon();
        ManualInspectionTest.RunTests();
    }
}
