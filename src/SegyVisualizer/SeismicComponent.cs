using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using R3;
using ScottPlot.Avalonia;
using ScottPlot.Panels;
using Seismic;

namespace AvaloniaPlot;

public class SeismicComponent : ComponentBase
{
    private readonly ReactiveProperty<int> _iLine = new(0);
    private readonly ReactiveProperty<int> _xLine = new(0);
    private SegyReader _segy = new(@"D:\TechApp\Reseis\Jerneh\Data\sgy\seismic_psdm_depth.sgy");
    private AvaPlot _avPlot = new();
    private ColorBar? _cb = null;
    private Slider _slider = new();
    private Slider _slider2 = new();


    protected override StyleGroup? BuildStyles() => [];// [new Style<Grid>().Background(Brushes.White)];

    protected override object Build() => new Grid().Rows("Auto, *, Auto").Children(
        new AvaPlot().Ref(out _avPlot).Row(1),
        new Border().Row(0).BorderThickness(0, 1).BorderBrush(Brushes.LightGray).Margin(0).Padding(10, 5).Child(
            new Grid().Cols("*, 50, *, 50").Children(
                new Slider().Minimum(_segy.InlineBegin).Maximum(_segy.InlineEnd).Ref(out _slider)
                    .Value(() => _iLine.Value, onChanged: v => _iLine.Value = (int)v),
                new TextBox().Col(1).Text(() => _iLine.Value.ToString()).OnTextChanged((e) => ParseText(e, _slider)),
                new Slider().Col(2).Margin(10, 0).Minimum(_segy.CrossLineBegin).Maximum(_segy.CrossLineEnd).Ref(out _slider2)
                    .Value(() => _xLine.Value, onChanged: v => _xLine.Value = (int)v),
                new TextBox().Col(3).Text(() => _xLine.Value.ToString()).OnTextChanged((e) => ParseText(e, _slider2))))
        );

    public SeismicComponent()
    {
        _iLine.Subscribe((i) => Plot(i));
        _xLine.Subscribe((x) => Plot(xLine: x));
    }

    void ParseText(TextChangedEventArgs e, Slider s) => s.Value = e.Source is TextBox v 
        && int.TryParse(v.Text, out var vi) && vi >= s.Minimum && vi <= s.Maximum ? vi : s.Value;

    void Plot(int iLine = -1, int xLine = -1)
    {
        var data = iLine > -1 ? InlinePlotData(_segy, iLine) : CrosslinePlotData(_segy, xLine);
        var plot = _avPlot.Plot;
        plot.Clear();
        if (_cb is not null)
            plot.Remove(_cb);
        plot.Axes.Margins(0, 0);
        plot.Axes.SetLimitsX(0, data.GetLength(1));
        plot.Axes.SetLimitsY(data.GetLength(0), 0);
        var hm = plot.Add.Heatmap(data);
        hm.Colormap = new ScottPlot.Colormaps.Grayscale();
        _cb = plot.Add.ColorBar(hm);
        _avPlot.Refresh();
    }

    public double[,] CrosslinePlotData(SegyReader r, int index = 0)
    {
        using var v = r.TraceByCrossline(index - _segy.CrossLineBegin);
        var min = v[v.Length - 2];
        var max = v[v.Length - 1];
        var o = new double[r.SampleSize, r.InLineSize];
        for (int j = 0, k = 0; j < o.GetLength(1); j++)
        {
            for (int i = 0; i < o.GetLength(0); i++, k++)
            {
                o[i, j] = v[k];
            }
        }
        return o;
    }

    public double[,] InlinePlotData(SegyReader r, int index = 0)
    {
        using var v = r.TraceByInline(index - _segy.InlineBegin);
        var min = v[v.Length - 2];
        var max = v[v.Length - 1];
        var o = new double[r.SampleSize, r.CrossLineSize];
        for (int j = 0, k = 0; j < o.GetLength(1); j++)
        {
            for (int i = 0; i < o.GetLength(0); i++, k++)
            {
                o[i, j] = v[k]; 
                //o[i, j] = v[k] / max; //(v[k] - min)/(max - min);
                //if (o[i, j] < 0.2)
                //    o[i, j] = 0;
            }
        }
        return o;
    }

    public static double[,] PlotData(ReadOnlySpan<float> v, int width, int height)
    {
        //var min = v[v.Length - 2];
        //var max = v[v.Length - 1];
        var o = new double[height, width];
        for (int j = 0, k = 0; j < o.GetLength(1); j++)
        {
            for (int i = 0; i < o.GetLength(0); i++, k++)
            {
                o[i, j] = v[k];
            }
        }
        return o;
    }
}