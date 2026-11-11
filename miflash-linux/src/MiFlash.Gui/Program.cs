using System;
using Avalonia;
using Avalonia.Threading;

namespace MiFlash.Gui;

/// <summary>
/// Point d'entrée du GUI (M6.3). Les opérations de flash tournent sur des
/// threads de fond via <see cref="ViewModels.MainViewModel"/> ; les rappels
/// UI passent par <see cref="Dispatcher.UIThread"/>.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
