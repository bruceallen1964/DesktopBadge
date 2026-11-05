using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBadge.Core;

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopBadge");

    public string FilePath => Path.Combine(Folder, "settings.json");

    public string LogPath => Path.Combine(Folder, "log.txt");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
