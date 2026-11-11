using System.Collections.Generic;
using System.ComponentModel;

namespace MiFlash.Gui.ViewModels;

/// <summary>
/// Base MVVM minimale (pas de toolkit externe) — les ViewModels ne référencent
/// pas Avalonia : les tests tournent headless.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>SetProperty : assigne et notifie si la valeur change.</summary>
    protected bool SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
