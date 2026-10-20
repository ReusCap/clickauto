using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoClickerMac.Core;

/// <summary>설정을 ~/Library/Application Support/AutoClickerMac/settings.json에 저장한다.</summary>
internal static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(SettingsDirectory(), "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string SettingsDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "AutoClickerMac");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClickerMac");
    }

    public static ClickerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<ClickerSettings>(File.ReadAllText(FilePath), Options);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 파일이 깨졌거나 읽을 수 없으면 기본값으로 시작한다.
        }
        return new ClickerSettings();
    }

    public static void Save(ClickerSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 치명적이지 않으므로 무시한다.
        }
    }
}
