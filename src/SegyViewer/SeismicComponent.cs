using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using R3;
using ScottPlot.Avalonia;
using ScottPlot.Panels;
using Seismic;

namespace SegyViewer;

public class SeismicComponent : ComponentBase
{
    private readonly BindableReactiveProperty<int> _iLine = new(0);
    private readonly BindableReactiveProperty<int> _xLine = new(0);
    private readonly BindableReactiveProperty<int> _zLine = new(0);
    private SegyReader _segy = new();
    private AvaPlot _avPlot = new();
    private ColorBar? _cb = null;
    private Slider _slider = new();
    private Slider _slider2 = new();
    private Slider _slider3 = new();

    public SeismicComponent()
    {
        _iLine.Subscribe((i) => Plot(i));
        _xLine.Subscribe((x) => Plot(xLine: x));
        _zLine.Subscribe((z) => Plot(zLine: z));
    }

    protected override StyleGroup? BuildStyles() => [];// [new Style<Grid>().Background(Brushes.White)];

    protected override object Build() => new Grid().Rows("Auto, *, Auto").Children(
        new AvaPlot().Ref(out _avPlot).Row(1),
        new Border().Row(0).BorderThickness(0, 1).BorderBrush(Brushes.LightGray).Margin(0).Padding(10, 5).Child(
            new Grid().Cols("50, *, 50, *, 50, *, 50").Children(
                new Button().Content("Open").OnClick(async(e) => await LoadFile()),
                new Slider().Col(1).Ref(out _slider).Value(() => _iLine.Value, onChanged: v => _iLine.Value = (int)v),
                new TextBox().Col(2).Text(() => _iLine.Value.ToString()).OnTextChanged((e) => ParseText(e, _slider)),
                new Slider().Col(3).Margin(10, 0).Ref(out _slider2).Value(() => _xLine.Value, onChanged: v => _xLine.Value = (int)v),
                new TextBox().Col(4).Text(() => _xLine.Value.ToString()).OnTextChanged((e) => ParseText(e, _slider2)),
                new Slider().Col(5).Margin(10, 0).Ref(out _slider3).Value(() => _zLine.Value, onChanged: v => _zLine.Value = (int)v),
                new TextBox().Col(6).Text(() => _zLine.Value.ToString()).OnTextChanged((e) => ParseText(e, _slider3))
            )
        )
    );

    async Task LoadFile()
    {
        if (!(TopLevel.GetTopLevel(this) is var t && t is not null))
            return;

        var o = new FilePickerOpenOptions { Title = "Load Segy" };
        var r = await t.StorageProvider.OpenFilePickerAsync(o);
        if (r.Count == 0)
            return;
        _segy = new(r[0].Path.AbsolutePath);
        _slider.Minimum(_segy.InlineBegin).Maximum(_segy.InlineEnd).Value(_segy.InlineBegin);
        _slider2.Minimum(_segy.CrossLineBegin).Maximum(_segy.CrossLineEnd).Value(_segy.CrossLineBegin);
        _slider3.Minimum(0).Maximum(_segy.SampleSize).Value(0);
    }

    void ParseText(TextChangedEventArgs e, Slider s) => s.Value = e.Source is TextBox v 
        && int.TryParse(v.Text, out var vi) && vi >= s.Minimum && vi <= s.Maximum ? vi : s.Value;

    void Plot(int iLine = -1, int xLine = -1, int zLine = -1)
    {
        if (_segy.FileName is null)
            return;
        var data = iLine > -1 ? PlotData(_segy.TraceByInline((iLine - _segy.InlineBegin)/_segy.InLineStep).AsSpan(), _segy.SampleSize, _segy.CrossLineSize, static (min, max, v) => Filter(min, max, v))
            : (xLine > -1 ? PlotData(_segy.TraceByCrossline((xLine - _segy.CrossLineBegin)/_segy.CrossLineStep).AsSpan(), _segy.SampleSize, _segy.InLineSize)
            : PlotData(_segy.TraceBySample(zLine).AsSpan(), _segy.CrossLineSize, _segy.InLineSize));
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

    static double Filter(float min, float max, float v, double filter = 0.2)
    {
        var o = v / Math.Max(Math.Abs(min), max); // (v - min) / (max - min);
        return o < filter ? 0 : o;
    }

    public static double[,] PlotData(ReadOnlySpan<float> v, int height, int width = 1, Func<float, float, float, double>? filter = null)
    {
        var o = new double[height, width];
        if (v.Length == 0)
            return o;
        var min = v[^2];
        var max = v[^1];
        for (int j = 0, k = 0; j < o.GetLength(1); j++)
        {
            for (int i = 0; i < o.GetLength(0); i++, k++)
            {
                o[i, j] = filter is null ? v[k] : filter.Invoke(min, max, v[k]);

            }
        }
        return o;
    }
}