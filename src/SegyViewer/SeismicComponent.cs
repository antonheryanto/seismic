using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using R3;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Panels;
using ScottPlot.Plottables;
using Seismic;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace SegyViewer;

public class SeismicComponent : ComponentBase
{
    private readonly ReactiveProperty<double> _min = new(0);
    private readonly ReactiveProperty<double> _max = new(1);
    private readonly ReactiveProperty<int> _iLine = new(0);
    private readonly ReactiveProperty<int> _xLine = new(0);
    private readonly ReactiveProperty<int> _zLine = new(0);
    private SegyReader _segy = new();
    private AvaPlot _avPlot = new();
    private ColorBar? _cb = null;
    private IColormap _colorMap = new ScottPlot.Colormaps.Turbo();
    private static readonly Dictionary<string, IColormap> _colorList = new IColormap[] {
        BlueRed(),
        new ScottPlot.Colormaps.Turbo(),
        new ScottPlot.Colormaps.Grayscale(),
        new ScottPlot.Colormaps.Jet(),
        new ScottPlot.Colormaps.Balance()
    }.ToDictionary(k => k.Name, v => v);

    private static ScottPlot.Colormaps.Custom BlueRed(int n = 256)
    {
        var colors = new ScottPlot.Color[n];
        var half = colors.Length / 2;
        for (int i = 0; i < half; i++)
        {
            colors[i] = ScottPlot.Colors.Blue.MixedWith(ScottPlot.Colors.White, (double)i / half);
            colors[i+half] = ScottPlot.Colors.White.MixedWith(ScottPlot.Colors.Red, (double)i / half);
        }
        return new ScottPlot.Colormaps.Custom(colors, "BlueRed");
    }

    public SeismicComponent()
    {
        _iLine.Subscribe((i) => Plot(i));
        _xLine.Subscribe((x) => Plot(xLine: x));
        _zLine.Subscribe((z) => Plot(zLine: z));
    }


    protected override StyleGroup? BuildStyles() => [
        new Style<TextBlock>().VerticalAlignment(VerticalAlignment.Center).Margin(5, 0),
        new Style<Slider>().VerticalAlignment(VerticalAlignment.Center),
        new Style<TextBox>().VerticalAlignment(VerticalAlignment.Center).Width(40).Margin(5, 0),
        new Style<ComboBox>().VerticalAlignment(VerticalAlignment.Center),
    ];

    protected override object Build() => new Grid().Rows("Auto, *, Auto").Children([
        new AvaPlot().Ref(out _avPlot).Row(1),
        new Border().Row(0).BorderThickness(0, 1).BorderBrush(Brushes.LightGray).Margin(0).Padding(10, 5).Child(
            new Grid().Cols($"Auto, *, *, *,Auto, Auto, Auto").Children([
                new Grid().Cols("Auto, *, Auto").Children([
                    new TextBlock().Text("Inline"),
                    new Slider().Col(1).Ref(out var s1).Value(() => _iLine.Value, onChanged: v => _iLine.Value = (int)v),
                    new TextBox().Col(2).Text(() => _iLine.Value.ToString(), onChanged: v => ParseText(v, s1)),
                ]).Col(1),
                new Grid().Cols("Auto, *, Auto").Children([
                    new TextBlock().Text("Crossline"),
                    new Slider().Col(1).Ref(out var s2).Value(() => _xLine.Value, onChanged: v => _xLine.Value = (int)v),
                    new TextBox().Col(2).Text(() => _xLine.Value.ToString(), onChanged: v => ParseText(v, s2)),
                ]).Col(2),
                new Grid().Cols("Auto, *, Auto").Children([
                    new TextBlock().Text("Sample"),
                    new Slider().Col(1).Ref(out var s3).Value(() => _zLine.Value, onChanged: v => _zLine.Value = (int)v),
                    new TextBox().Col(2).Text(() => _zLine.Value.ToString(), onChanged: v => ParseText(v, s3)),
                ]).Col(3),
                new Button().Content("Open").OnClick(async (e) => await LoadFile(s1, s2, s3)),
                new ComboBox().Col(4).ItemsSource(_colorList.Keys).SelectedItem(() => _colorMap.Name, onChanged: ChangeColorMap),
                new TextBox().Col(5).Text(() => _min.Value.ToString()).Ref(out var minT).OnLostFocus(e => UpdateRange(min:minT.Text)),
                new TextBox().Col(6).Text(() => _max.Value.ToString()).Ref(out var maxT).OnLostFocus(e => UpdateRange(max:maxT.Text)),
            ])
        )
    ]);

    private void UpdateRange(string? min = null, string? max = null)
    {
        if (!(_avPlot.Plot.PlottableList.FirstOrDefault(f => f is Heatmap) is Heatmap hm && hm is not null))
            return;
        if (!double.TryParse(min, out var minD))
            minD = _min.Value;
        if (!double.TryParse(max, out var maxD))
            maxD = _max.Value;
        _min.Value = Math.Min(minD, maxD);
        _max.Value = Math.Max(minD, maxD);
        hm.ManualRange = new(_min.Value, _max.Value);
        _avPlot.Refresh();
    }

    private void ChangeColorMap(object o)
    {
        if (o is not string color || color is null || !_colorList.TryGetValue(color, out var cm))
            return;
        _colorMap = cm;
        Plot(_iLine.Value);
    }

    private async Task LoadFile(Slider si, Slider sx, Slider sz)
    {
        if (!(TopLevel.GetTopLevel(this) is var t) || t is null)
            return;
        var o = new FilePickerOpenOptions { Title = "Load Segy" };
        var r = await t.StorageProvider.OpenFilePickerAsync(o);
        if (r.Count == 0)
            return;
        try
        {
            _segy = new(r[0].Path.LocalPath);
            si.Minimum(_segy.InlineBegin).Maximum(_segy.InlineEnd).Value(_segy.InlineBegin);
            sx.Minimum(_segy.CrossLineBegin).Maximum(_segy.CrossLineEnd).Value(_segy.CrossLineBegin);
            sz.Minimum(0).Maximum(_segy.SampleSize).Value(0);
        } catch (Exception ex) {
            var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, MsBox.Avalonia.Enums.ButtonEnum.YesNo);
            var result = await box.ShowAsync();
        }
    }

    private static void ParseText(string v, Slider s) => 
        s.Value = int.TryParse(v, out var vi) && vi >= s.Minimum && vi <= s.Maximum ? vi : s.Value;

    private void Plot(int iLine = -1, int xLine = -1, int zLine = -1)
    {
        if (_segy.FileName is null)
            return;
        var data = iLine > -1 ? PlotData(_segy.TraceByInline((iLine - _segy.InlineBegin) / _segy.InLineStep).AsSpan(), _segy.SampleSize, _segy.CrossLineSize)
            : (xLine > -1 ? PlotData(_segy.TraceByCrossline((xLine - _segy.CrossLineBegin) / _segy.CrossLineStep).AsSpan(), _segy.SampleSize, _segy.InLineSize)
            : PlotData(_segy.TraceBySample(zLine).AsSpan(), _segy.CrossLineSize, _segy.InLineSize));
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
        hm.ManualRange = new(_min.Value, _max.Value);
        _avPlot.Refresh();
    }

    // (v - min) / (max - min);
    //private static double Filter(float min, float max, float v, double filter = 0.2)
    //    => (float)(v / Math.Max(Math.Abs(min), max)) < filter ? 0 : (float)(v / Math.Max(Math.Abs(min), max));

    public static double[,] PlotData(ReadOnlySpan<float> v, int height, int width = 1)
    {
        var o = new double[height, width];
        if (v.Length == 0)
            return o;
        //var min = v[^2];
        //var max = v[^1];
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