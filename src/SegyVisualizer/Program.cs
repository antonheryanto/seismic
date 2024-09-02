using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using R3;
using System;

namespace AvaloniaPlot;

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
            //.Start(static (app, args) => app.Run(new Program()), args);
            .SetupWithLifetime(lifetime);

        lifetime.MainWindow = new Program();
        lifetime.Start(args);
    }

    private readonly R3.Avalonia.AvaloniaRenderingFrameProvider frameProvider;
    public Program()
    {
        Title = "Avalonia Hello";
        Width = 1080;
        Height = 720;
        Content = new SeismicComponent();

        var topLevel = GetTopLevel(this);
        frameProvider = new (topLevel!);
    }

    // pass frameProvider
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        Observable.EveryValueChanged(this, x => x.Width, frameProvider)
            .Subscribe(x => Title = $"Width of {x}");
    }

    protected override void OnClosed(EventArgs e)
    {
        frameProvider.Dispose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
            desktopApp.Shutdown();
    }

    
}