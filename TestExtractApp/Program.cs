using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
        HikApiTest.PrintApi();
        CameraTest.TestCameraParametersJobSerialization();
        CaliperAndLineTest.RunTests();
        ColorDiffTest.RunTests();
        RecentJobsAndCalibrationTest.RunTests();
        IconGenerator.GenerateAppIcons();
        IconGenerator.VerifyExeIcon();
    }
}
