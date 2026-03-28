using RetroJukebox.ViewModels;
using System.Windows.Controls;

namespace RetroJukebox.Views;

public partial class EqualizerPanel : UserControl
{
    public EqualizerPanel()
    {
        InitializeComponent();
    }

    // Route ComboBox selection to the RelayCommand to avoid binding loops
    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EqualizerViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is string preset)
            vm.ApplyPresetCommand.Execute(preset);
    }
}
