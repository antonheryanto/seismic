using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;

namespace Seismic.Sample;

class Program
{
    static void Main(string[] args)
    {
        //var reader = new SegyReader(@"C:\Projects\TechApps\entd\data\F3_demo.sgy");
        var summary = BenchmarkRunner.Run<SeismicBenchmark>();
    }
}

[MemoryDiagnoser]
public class SeismicBenchmark
{
    const string FILE = @"C:\Projects\TechApps\entd\data\F3_demo.sgy";

    [Benchmark]
    public float[][] ArrayBased()
    {
        var reader = new TechApps.Seismic.SegyReader(FILE);
        return reader.Traces;
    }

    [Benchmark]
    public float[][] SpanBased()
    {
        var reader = new SegyReader(FILE);
        return reader.ReadAllTraces();
    }

}
