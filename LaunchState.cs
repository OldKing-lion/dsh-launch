using System.Diagnostics;
using System.Text.Json;

namespace DshRepoShell;

sealed class LaunchState
{
    public int Pid { get; set; }
    public string AuthenticatedUrl { get; set; } = "";

    static string PathName => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dsh-repo-shell",
        "launch-state.json");

    public static LaunchState? Load()
    {
        try
        {
            if (!File.Exists(PathName)) return null;
            return JsonSerializer.Deserialize<LaunchState>(File.ReadAllText(PathName));
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(PathName)) File.Delete(PathName);
        }
        catch
        {
            // Stale state is only a reuse hint; failing to delete must not block exit.
        }
    }

    public bool MatchesLiveProcess()
    {
        if (Pid <= 0 || string.IsNullOrWhiteSpace(AuthenticatedUrl)) return false;
        try
        {
            var live = Process.GetProcessById(Pid);
            return !live.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public Process? TryGetLiveProcess()
    {
        if (!MatchesLiveProcess()) return null;
        try
        {
            return Process.GetProcessById(Pid);
        }
        catch
        {
            return null;
        }
    }
}
