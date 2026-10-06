using Newtonsoft.Json.Linq;
using System.IO;

namespace ForeSITETestApp;

internal static class AppPaths
{
    public static string UserDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForeSITE");
    public static string DatabasePath => Path.Combine(UserDataDirectory, "Data", "foresite_alerting.db");
    public static string ConfigPath => Path.Combine(UserDataDirectory, "config.json");
    public static string LlmConfigPath => Path.Combine(UserDataDirectory, "llm_config.json");
    public static string LogPath => Path.Combine(UserDataDirectory, "Logs", "flask_log.txt");
    public static string RUserPath => Path.Combine(UserDataDirectory, "R");
    public static string RLibraryPath => Path.Combine(RUserPath, "library");
    public static string ReportsDirectory => Path.Combine(UserDataDirectory, "Reports");
    public static string ReportDefinitionsDirectory => Path.Combine(ReportsDirectory, "Definitions");
    public static string BundledServerDirectory => Path.Combine(AppContext.BaseDirectory, "Server");
    public static string BundledConfigPath => Path.Combine(BundledServerDirectory, "config.json");
    public static string LegacyDatabasePath => Path.Combine(BundledServerDirectory, "foresite_alerting.db");

    public static void EnsureInitialized()
    {
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Directory.CreateDirectory(ReportDefinitionsDirectory);

        if (!File.Exists(DatabasePath) && File.Exists(LegacyDatabasePath))
            File.Copy(LegacyDatabasePath, DatabasePath, overwrite: false);

        if (!File.Exists(ConfigPath))
        {
            JObject config = File.Exists(BundledConfigPath)
                ? JObject.Parse(File.ReadAllText(BundledConfigPath))
                : new JObject();
            File.WriteAllText(ConfigPath, config.ToString());
        }

        // Remove credentials left by older releases; SMTP secrets are supplied
        // through FORESITE_SMTP_PASSWORD and are never persisted by the app.
        JObject userConfig = JObject.Parse(File.ReadAllText(ConfigPath));
        if (!string.IsNullOrEmpty(userConfig.Value<string>("password")) ||
            userConfig["passwordEnvironmentVariable"]?.ToString() != "FORESITE_SMTP_PASSWORD")
        {
            userConfig["password"] = "";
            userConfig.Remove("passwordProtected");
            userConfig["passwordEnvironmentVariable"] = "FORESITE_SMTP_PASSWORD";
            File.WriteAllText(ConfigPath, userConfig.ToString());
        }

        string legacyLlmConfig = Path.Combine(AppContext.BaseDirectory, "llm_config.json");
        if (!File.Exists(LlmConfigPath) && File.Exists(legacyLlmConfig))
            File.Copy(legacyLlmConfig, LlmConfigPath, overwrite: false);
    }
}
