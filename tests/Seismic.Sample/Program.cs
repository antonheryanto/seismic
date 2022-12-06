using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Threading.Tasks;

namespace Seismic.Sample;

class Program
{
    static void Main(string[] args)
    {
        //var reader = new SegyReader(@"C:\Projects\TechApps\entd\data\F3_demo.sgy");
        //var traces = await reader.ReadTraceAsync();
        var summary = BenchmarkRunner.Run<SeismicBenchmark>();
    }
}

[MemoryDiagnoser]
public class SeismicBenchmark
{
    const string FILE = @"C:\Projects\TechApps\entd\data\F3_demo.sgy";

    //[Benchmark]
    public float[][] ArrayBased()
    {
        var reader = new TechApps.Seismic.SegyReader(FILE);
        return reader.Traces;
    }

    //[Benchmark]
    public float[][] SpanBased()
    {
        var reader = new SegyReader(FILE);
        return reader.ReadAllTraces();
    }

    //[Params(32 * 1024 * 1024, 8 * 1024 * 1024, 1024 * 1024, 512 * 1024)]
    [Params(1024 * 1024)]
    public int MinSize { get; set; }

    [Benchmark]
    public async Task<float[][]> SpanAsyncBased()
    {
        var reader = new SegyReader(FILE);
        return await reader.ReadTraceAsync(MinSize);
    }

}
