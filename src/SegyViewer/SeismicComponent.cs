using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using R3;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Panels;
using Seismic;

namespace SegyViewer;

public class SeismicComponent : ComponentBase
{
    private const int TEXT_WIDTH = 80;
    private readonly BindableReactiveProperty<int> _iLine = new(0);
    private readonly BindableReactiveProperty<int> _xLine = new(0);
    private readonly BindableReactiveProperty<int> _zLine = new(0);
    private SegyReader _segy = new();
    private AvaPlot _avPlot = new();
    private ColorBar? _cb = null;
    private IColormap _colorMap = new ScottPlot.Colormaps.Grayscale();
    private static readonly Dictionary<string, IColormap> _colorList = new IColormap[] {
        new ScottPlot.Colormaps.Grayscale(),
        new ScottPlot.Colormaps.Jet(),
        new ScottPlot.Colormaps.Balance()
    }.ToDictionary(k => k.Name, v => v);


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
            new Grid().Cols($"*, *, {TEXT_WIDTH}, *, {TEXT_WIDTH}, *, {TEXT_WIDTH},*").Children([
                new Slider().Col(1).Ref(out var _slider).Value(() => _iLine.Value, onChanged: v => _iLine.Value = (int)v).Margin(10, 0),
                new Slider().Col(3).Ref(out var _slider2).Value(() => _xLine.Value, onChanged: v => _xLine.Value = (int)v).Margin(10, 0),
                new Slider().Col(5).Ref(out var _slider3).Value(() => _zLine.Value, onChanged: v => _zLine.Value = (int)v).Margin(10, 0),
                new Button().Content("Open").OnClick(async (e) => await LoadFile(_slider, _slider2, _slider3)),
                new TextBox().Col(2).Text(() => _iLine.Value.ToString(), onChanged: v => ParseText(v, _slider)),
                new TextBox().Col(4).Text(() => _xLine.Value.ToString(), onChanged: v => ParseText(v, _slider2)),
                new TextBox().Col(6).Text(() => _zLine.Value.ToString(), onChanged: v => ParseText(v, _slider3)),
                new ComboBox().Col(7).ItemsSource(_colorList.Keys).SelectedItem(() => _colorMap.Name, onChanged: ChangeColorMap),
            ])
        )
    );

    private void ChangeColorMap(object o)
    {
        if (o is not string color || color is null || !_colorList.TryGetValue(color, out var cm))
            return;
        _colorMap = cm;
        Plot(_iLine.Value);
    }

    private async Task LoadFile(Slider si, Slider sx, Slider sz)
    {
        if (!(TopLevel.GetTopLevel(this) is var t && t is not null))
            return;

        var o = new FilePickerOpenOptions { Title = "Load Segy" };
        var r = await t.StorageProvider.OpenFilePickerAsync(o);
        if (r.Count == 0)
            return;
        _segy = new(r[0].Path.AbsolutePath);
        si.Minimum(_segy.InlineBegin).Maximum(_segy.InlineEnd).Value(_segy.InlineBegin);
        sx.Minimum(_segy.CrossLineBegin).Maximum(_segy.CrossLineEnd).Value(_segy.CrossLineBegin);
        sz.Minimum(0).Maximum(_segy.SampleSize).Value(0);
    }

    private static void ParseText(string v, Slider s) => 
        s.Value = int.TryParse(v, out var vi) && vi >= s.Minimum && vi <= s.Maximum ? vi : s.Value;

    private void Plot(int iLine = -1, int xLine = -1, int zLine = -1)
    {
        if (_segy.FileName is null)
            return;
        var data = iLine > -1 ? PlotData(_segy.TraceByInline((iLine - _segy.InlineBegin) / _segy.InLineStep).AsSpan(), _segy.SampleSize, _segy.CrossLineSize)//, static (min, max, v) => Filter(min, max, v))
            : (xLine > -1 ? PlotData(_segy.TraceByCrossline((xLine - _segy.CrossLineBegin) / _segy.CrossLineStep).AsSpan(), _segy.SampleSize, _segy.InLineSize)//, static (min, max, v) => Filter(min, max, v))
            : PlotData(_segy.TraceBySample(zLine).AsSpan(), _segy.CrossLineSize, _segy.InLineSize));//, static (min, max, v) => Filter(min, max, v)));
        var plot = _avPlot.Plot;
        plot.Clear();
        if (_cb is not null)
            plot.Remove(_cb);
        plot.Axes.Margins(0, 0);
        plot.Axes.SetLimitsX(0, data.GetLength(1));
        plot.Axes.SetLimitsY(data.GetLength(0), 0);
        var hm = plot.Add.Heatmap(data);
        hm.Colormap = _colorMap;
        _cb = plot.Add.ColorBar(hm);
        _avPlot.Refresh();
    }

    // (v - min) / (max - min);
    //private static double Filter(float min, float max, float v, double filter = 0.2)
    //    => (float)(v / Math.Max(Math.Abs(min), max)) < filter ? 0 : (float)(v / Math.Max(Math.Abs(min), max));

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