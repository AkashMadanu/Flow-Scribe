using System.Text.Json;
using FlowScribe.Core;
using Microsoft.Win32;

namespace FlowScribe;

/// <summary>The few things FlowScribe remembers, in %APPDATA%\FlowScribe\settings.json.</summary>
sealed class Settings
{
    /// Chosen microphone by name; null follows the system default.
    public string? Microphone { get; set; }
    public bool StartupRegistered { get; set; }

    static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { }
    }

    /// Start at login, set up once on first launch. If the user later turns it
    /// off (Task Manager > Startup apps), that choice is kept.
    public void RegisterStartupOnce()
    {
        if (StartupRegistered || Environment.ProcessPath is not { } exe) return;
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            run.SetValue("FlowScribe", $"\"{exe}\"");
            StartupRegistered = true;
            Save();
        }
        catch (Exception) { }
    }
}
