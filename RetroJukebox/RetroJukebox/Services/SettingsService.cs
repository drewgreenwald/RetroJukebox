using System.IO;

namespace RetroJukebox.Services;

public class SettingsService
{
    private readonly string _path;
    private Dictionary<string, object> _data = [];

    public SettingsService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RetroJukebox");
        Directory.CreateDirectory(appData);
        _path = Path.Combine(appData, "settings.json");
        Load();
    }

    public T Get<T>(string key, T defaultValue)
    {
        if (_data.TryGetValue(key, out var val))
        {
            try { return (T)Convert.ChangeType(val, typeof(T))!; }
            catch { return defaultValue; }
        }
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        _data[key] = value!;
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            _data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? [];
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_data, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_path, json);
        }
        catch { }
    }
}
