using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MiFlash.Gui.ViewModels;

namespace MiFlash.Gui;

/// <summary>Application Avalonia — thème Fluent, fenêtre principale.</summary>
public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(uiPost: action => Dispatcher.UIThread.Post(action));
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            // Sélecteur de répertoire : le VM émet l'événement, la fenêtre
            // possède le StorageProvider (seule dépendance Avalonia du flux).
            viewModel.BrowseRequested += async () => await window.BrowseDirectoryAsync();
            desktop.MainWindow = window;
            viewModel.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
