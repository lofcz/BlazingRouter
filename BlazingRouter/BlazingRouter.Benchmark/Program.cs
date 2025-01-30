using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;

namespace BlazingRouter.Benchmark;

class Program
{
    static void Main(string[] args)
    {
        ManualConfig config = DefaultConfig.Instance
            .AddColumn(StatisticColumn.Mean)
            .AddColumn(StatisticColumn.StdDev)
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .AddDiagnoser(MemoryDiagnoser.Default);

        BenchmarkRunner.Run<RouterBenchmarks>(config);
    }
}