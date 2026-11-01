namespace FlowScribe.Core;

public static class AppPaths
{
    /// %APPDATA%\FlowScribe on Windows: history, word list and settings.
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowScribe");

    /// %LOCALAPPDATA%\FlowScribe\Models on Windows: the speech model (not roamed).
    public static string ModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowScribe", "Models");
}
