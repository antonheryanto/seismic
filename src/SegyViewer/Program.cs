using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SegyViewer;

internal class Program : Window
{
    public static void Main(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnLastWindowClose };
        AppBuilder
            .Configure<Application>()
            .UsePlatformDetect()
            .AfterSetup(b => b.Instance?.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme()))
            .UseR3()
            .SetupWithLifetime(lifetime);

        lifetime.MainWindow = new Program();
        lifetime.Start(args);
    }

    private readonly R3.Avalonia.AvaloniaRenderingFrameProvider frameProvider;
    public Program()
    {
        Title = "Segy Viewer";
        Width = 1080;
        Height = 720;
        Content = new SeismicComponent();

        frameProvider = new (GetTopLevel(this)!);
    }

    // pass frameProvider
    //protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    //    => Observable.EveryValueChanged(this, x => x.Width, frameProvider).Subscribe(x => Title = $"Width of {x}");

    protected override void OnClosed(EventArgs e)
    {
        frameProvider.Dispose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
            desktopApp.Shutdown();
    }
}