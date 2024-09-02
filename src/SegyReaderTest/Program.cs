using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System;
using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace Seismic.Sample;

class Program
{
    static void Main(string[] args)
    {
        var a = @"C:\Projects\TechApps\RockSeisCloud\data\data\2D_seismic\2D_07-14.sgy";
        var d = @"C:\Projects\TechApps\ReSeis\data\Baram_Res\pp_psdm\new_cropped_scaled_apsdm_2017.sgy";
        //var b = @"C:\Projects\TechApps\RockSeismod\data\Kirchhoff_PreSTM_time.segy";
        //var c = @"C:\Projects\TechApps\RockSeismod\data\T07_Angsi_stack_0_48deg_LineERivisit_E02.sgy";
        var reader = new SegyReader(d);
        //using var traces = reader.GetTraces([1,2,3,10,11,12,16,17,18]);
        //using var traces = reader.GetTraces([[1,2,3],[10,11,12],[16,17,18]]);
        //using var traces = reader.GetTraces([(..3)]);
        //using var traces = reader.TraceByCrossline(1, 2);
        using var traces = reader.TraceBySample();
        var T = traces.AsSpan();
        //using var traces = reader.AsNativeMemoryArray();
        //reader.Write(d, traces);
        //var traces = reader.ReadAllTraces();
        //reader.Write(d, traces);
        //var v = IbmToFloat3();
        //var summary = BenchmarkRunner.Run<SeismicBenchmark>();
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

    //[Params(32 * 1024 * 1024, 8 * 1024 * 1024, 1024 * 1024, 512 * 1024)]
    [Params(1024 * 1024)]
    public int MinSize { get; set; }

}
