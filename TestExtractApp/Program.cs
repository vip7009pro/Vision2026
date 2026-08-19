using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
        CaliperAndLineTest.RunTests();
        VariableInjectionTest.RunTests();
        ContinuousPipelineTest.RunTestsAsync().GetAwaiter().GetResult();
        PlcBridgeTest.RunTestsAsync().GetAwaiter().GetResult();
    }
}
