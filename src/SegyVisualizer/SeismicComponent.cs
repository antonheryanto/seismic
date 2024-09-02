using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using R3;
using ScottPlot.Avalonia;
using Seismic;

namespace AvaloniaPlot;

public class SeismicComponent : ComponentBase
{
    private readonly ReactiveProperty<int> _inline = new(10);
    //private readonly SeismicModel _m = new();
    SegyReader _segy = new(@"C:\Projects\TechApps\ReSeis\data\Jerneh\sgy\seismic_psdm_depth_sfre.sgy");
    AvaPlot _avPlot;

    protected override object Build() => new Grid().Rows("50, *").Children(new AvaPlot().Ref(out _avPlot).Row(1),
        new Grid().Cols("50, *, 50").Children(
            new TextBox().Margin(5).Text(() => _inline.Value.ToString()),
                //.OnTextChanged((v) => _inline.Value = int.TryParse(v.Source., out var vi) ? vi : _inline.Value),
            new Slider().Minimum(_segy.InlineBegin).Maximum(_segy.InlineEnd)
                .Value(() => _inline.Value, onChanged: v => _inline.Value = (int) v).Col(1),
            new TextBox().Margin(15).Col(2)));

    public SeismicComponent()
    {
        _inline.Subscribe((x) => Plot(x));
        Plot(first: true);
    }

    void Plot(int inLine = 0, bool first = false)
    {
        if (inLine < _segy.InlineBegin)
            inLine = _segy.InlineBegin;
        if (inLine > _segy.InlineEnd)
            inLine = _segy.InlineEnd;
        var x = InlinePlotData(_segy, inLine - _segy.InlineBegin);
        var plot = _avPlot.Plot;
        plot.Clear();
        plot.Axes.Margins(0, 0);
        plot.Axes.SetLimitsY(bottom: _segy.SampleSize, top: 0);
        var hm = plot.Add.Heatmap(x);
        hm.Colormap = new ScottPlot.Colormaps.Grayscale();
        if (first)
            plot.Add.ColorBar(hm);
        _avPlot.Refresh();
    }

    public static double[,] InlinePlotData(SegyReader r, int inline = 0)
    {
        using var v = r.TraceByInline(inline);
        var min = v[v.Length - 2];
        var max = v[v.Length - 1];
        var o = new double[r.SampleSize, r.CrossLineSize];
        for (int j = 0, k = 0; j < o.GetLength(1); j++)
        {
            for (int i = 0; i < o.GetLength(0); i++, k++)
            {
                o[i, j] = v[k] / max; //(v[k] - min)/(max - min);
                if (o[i, j] < 0.2)
                    o[i, j] = 0;
            }
        }
        return o;
    }

}