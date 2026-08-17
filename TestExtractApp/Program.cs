using System;

namespace TestExtractApp;

class Program
{
    static void Main(string[] args)
    {
        VariableInjectionTest.RunTests();
        ContinuousPipelineTest.RunTestsAsync().GetAwaiter().GetResult();
    }
}
