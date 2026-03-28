using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace RetroJukebox.Models;

/// <summary>
/// ObservableCollection that suppresses per-item notifications during bulk operations,
/// firing a single Reset at the end instead of N individual Add notifications.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }

    /// <summary>Clears the collection and replaces all items, firing one Reset.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        _suppressNotifications = true;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        _suppressNotifications = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }
}
