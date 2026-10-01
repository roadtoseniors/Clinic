using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Clinic.Preferences;

public static class ThemePreferenceStore
{
    private static readonly Dictionary<string, bool> BrowserPreferences = new(StringComparer.Ordinal);

    public static bool Get(string key, bool fallback = false)
    {
        if (OperatingSystem.IsBrowser())
            return BrowserPreferences.TryGetValue(key, out var browserValue) ? browserValue : fallback;

        try
        {
            var path = FilePath();
            if (!File.Exists(path)) return fallback;
            var preferences = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path));
            return preferences is not null && preferences.TryGetValue(key, out var value) ? value : fallback;
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (JsonException) { return fallback; }
    }

    public static bool Save(string key, bool isDark)
    {
        if (OperatingSystem.IsBrowser())
        {
            BrowserPreferences[key] = isDark;
            return true;
        }

        try
        {
            var path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var preferences = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path)) ?? new()
                : new Dictionary<string, bool>(StringComparer.Ordinal);
            preferences[key] = isDark;
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private static string FilePath() =>
        Environment.GetEnvironmentVariable("CLINIC_THEME_PREFERENCES_PATH") is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clinic", "theme-preferences.json");
}
