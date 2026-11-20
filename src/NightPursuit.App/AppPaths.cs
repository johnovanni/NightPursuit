using System.IO;

namespace NightPursuit.App;

/// <summary>Where the game keeps its files. Only the App reads this; Core is handed the folder (and the diagnostics never use it).</summary>
public static class AppPaths
{
    /// <summary><c>%AppData%\NightPursuit</c>: settings.json and highscores.json.</summary>
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NightPursuit");
}
