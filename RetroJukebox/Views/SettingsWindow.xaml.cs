using RetroJukebox.Services;
using System.Windows;
using System.Windows.Controls;

namespace RetroJukebox.Views;

public partial class SettingsWindow : Window
{
    private readonly AudioService _audio;

    public SettingsWindow()
    {
        InitializeComponent();
        _audio = App.AudioService;
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        // Output devices
        var devices = AudioService.GetAvailableDevices();
        DeviceCombo.ItemsSource = devices;
        var savedDevice = _audio.SelectedDeviceName;
        if (!string.IsNullOrEmpty(savedDevice) && devices.Contains(savedDevice))
            DeviceCombo.SelectedItem = savedDevice;
        else if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;

        // ASIO devices
        var asioDevices = AudioService.GetAsioDevices();
        AsioCombo.ItemsSource = asioDevices.Count > 0 ? asioDevices : new List<string> { "(No ASIO drivers found)" };
        AsioCombo.SelectedIndex = 0;

        // Output mode — match by Tag value, not index (items are not in enum order)
        var targetMode = (int)_audio.OutputDeviceType;
        foreach (ComboBoxItem item in OutputModeCombo.Items)
        {
            if (item.Tag is string tagStr && tagStr == targetMode.ToString())
            {
                OutputModeCombo.SelectedItem = item;
                break;
            }
        }

        // Playback
        CrossfadeCheck.IsChecked = _audio.CrossfadeEnabled;
        CrossfadeBox.Text = _audio.CrossfadeDuration.ToString("F1");
        GaplessCheck.IsChecked = _audio.GaplessEnabled;

        // EQ
        EqCheck.IsChecked = _audio.EqEnabled;
        var savedPreset = App.SettingsService.Get("EqPresetIndex", 0);
        EqPresetCombo.SelectedIndex = Math.Clamp(savedPreset, 0, EqPresetCombo.Items.Count - 1);
    }

    private void EqPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EqPresetCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            int.TryParse(tagStr, out var idx))
        {
            _audio.SetEqPreset((EqPreset)idx);
        }
    }

    private void Apply(object sender, RoutedEventArgs e)
    {
        // Output mode
        if (OutputModeCombo.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeStr &&
            int.TryParse(modeStr, out var modeIdx))
        {
            var newMode = (OutputDeviceType)modeIdx;
            _audio.OutputDeviceType = newMode;

            // Set the device name appropriate for the selected mode
            if (newMode == OutputDeviceType.Asio)
            {
                if (AsioCombo.SelectedItem is string asioName)
                    _audio.SelectedDeviceName = asioName;
            }
            else
            {
                if (DeviceCombo.SelectedItem is string deviceName)
                    _audio.SelectedDeviceName = deviceName;
            }
        }

        // Playback settings
        _audio.CrossfadeEnabled = CrossfadeCheck.IsChecked == true;
        if (double.TryParse(CrossfadeBox.Text, out var cf))
            _audio.CrossfadeDuration = Math.Clamp(cf, 0.5, 15.0);
        _audio.GaplessEnabled = GaplessCheck.IsChecked == true;

        // EQ
        _audio.EqEnabled = EqCheck.IsChecked == true;
        App.SettingsService.Set("EqPresetIndex", EqPresetCombo.SelectedIndex);

        _audio.SaveSettings();

        MessageBox.Show("Settings applied. Audio device changes will take effect when the app is restarted.",
            "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseBtn(object sender, RoutedEventArgs e) => Close();
}
