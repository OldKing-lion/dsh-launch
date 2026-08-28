using System.Text.Json;

namespace DshRepoShell;

sealed class AppConfig
{
    public string RepoRoot { get; set; } = @"C:\Repo\K2Mobilization\deepseek-harness";
    public int Port { get; set; } = 3080;
    public string Profile { get; set; } = "web";
    public string NodeExe { get; set; } = "";

    public static AppConfig Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (loaded is not null) return loaded;
        }
        return new AppConfig();
    }

    static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh-repo-shell",
            "config.json");
        yield return Path.Combine(AppContext.BaseDirectory, "config.json");
    }
}
