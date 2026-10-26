using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DeepcoolLm240Gui;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    void OnTrayClicked(object? sender, EventArgs e) => ShowMain();

    void OnOpenClicked(object? sender, EventArgs e) => ShowMain();

    void OnExitClicked(object? sender, EventArgs e) => Exit();

    public static void ShowMain()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        if (desktop.MainWindow is not MainWindow window)
            return;
        window.ShowFromTray();
    }

    public static void Exit()
    {
        IsExiting = true;
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
