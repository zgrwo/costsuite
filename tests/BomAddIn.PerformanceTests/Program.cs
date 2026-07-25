using BenchmarkDotNet.Running;

namespace BomAddIn.PerformanceTests;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
