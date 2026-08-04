using System;
using System.Threading.Tasks;

namespace TestExtractApp;

class Program
{
    static async Task Main(string[] args)
    {
        await PlcTests.RunAllTestsAsync();
    }
}
