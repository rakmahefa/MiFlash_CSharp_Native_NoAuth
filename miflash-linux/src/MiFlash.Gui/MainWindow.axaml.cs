using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace MiFlash.Gui;

/// <summary>
/// Fenêtre principale : seul le sélecteur de répertoire touche à Avalonia
/// (<see cref="StorageProvider"/>, appelé via <see cref="BrowseDirectoryAsync"/>)
/// ; tout le reste est piloté par les ViewModels (testables headless).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Ouvre le sélecteur de répertoire et pousse le choix au
    /// <see cref="ViewModels.MainViewModel.RomDirectory"/>.
    /// </summary>
    public async Task BrowseDirectoryAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Répertoire de la ROM (images + XML + programmer)",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && DataContext is ViewModels.MainViewModel vm)
        {
            try
            {
                vm.RomDirectory = folders[0].Path.LocalPath;
            }
            catch (InvalidOperationException)
            {
                // Chemin non local (rare) : ignoré.
            }
        }
    }
}
