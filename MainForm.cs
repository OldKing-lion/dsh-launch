using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DshRepoShell;

/// <summary>
/// Thin desktop window around the git-checkout <c>pnpm dsh web</c> page.
/// Closing the window hides to the tray; only 退出 stops this shell.
/// The dsh process is left running so a Job object cannot take it down with us.
/// </summary>
sealed class MainForm : Form
{
    static readonly Regex ReadyUrl = new(
        @"https?://127\.0\.0\.1:\d+/\S*token=\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Ansi = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dsh-repo-shell");
    static readonly string UserDataDir = Path.Combine(DataDir, "webview");
    static readonly string LogPath = Path.Combine(DataDir, "dsh-web.log");

    readonly AppConfig _config = AppConfig.Load();
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 12f),
        BackColor = Color.White,
        ForeColor = Color.FromArgb(40, 40, 40),
        Text = "正在启动 dsh web…",
    };
    readonly NotifyIcon _tray;
    readonly Icon _icon;
    bool _reallyExit;

    public MainForm()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "whale-black.ico");
        _icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application;

        Text = "DeepSeek Harness";
        Icon = _icon;
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 560);

        Controls.Add(_status);
        Controls.Add(_web);
        _status.BringToFront();

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add("退出", null, (_, _) => Quit());
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "DeepSeek Harness",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        FormClosing += OnFormClosing;
        Shown += async (_, _) => await BootAsync();
    }

    void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }
        _status.Text = text;
        _status.Visible = true;
        _status.BringToFront();
    }

    async Task BootAsync()
    {
        try
        {
            Directory.CreateDirectory(UserDataDir);
            SetStatus("正在初始化窗口…");
            var env = await CoreWebView2Environment.CreateAsync(null, UserDataDir);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                {
                    _status.Visible = false;
                    return;
                }
                SetStatus($"页面加载失败（0x{e.WebErrorStatus:X}）。日志：{LogPath}");
            };

            var url = await EnsureDshUrlAsync();
            SetStatus("正在打开页面…");
            _web.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(
                this,
                ex.Message + "\n\n日志：" + LogPath,
                "DeepSeek Harness 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    async Task<string> EnsureDshUrlAsync()
    {
        while (await PortOpenAsync(_config.Port))
        {
            var saved = LaunchState.Load();
            if (saved?.MatchesLiveProcess() == true
                && saved.AuthenticatedUrl.Contains("token=", StringComparison.Ordinal))
            {
                return saved.AuthenticatedUrl;
            }

            var choice = MessageBox.Show(
                this,
                $"端口 {_config.Port} 已被占用，但本窗口没有那次 dsh web 的登录 token。\n\n"
                + "请先关掉终端里已经在跑的 pnpm dsh web，然后点「重试」。\n"
                + "本程序需要自己拉起 dsh，才能打开带 token 的地址。",
                "需要重新拉起 dsh web",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Retry)
            {
                throw new InvalidOperationException("已取消。关掉原来的 dsh web 后再打开本程序。");
            }
        }

        return await StartDshAndWaitForTokenAsync();
    }

    async Task<string> StartDshAndWaitForTokenAsync()
    {
        var node = string.IsNullOrWhiteSpace(_config.NodeExe)
            ? Environment.GetEnvironmentVariable("DSH_NODE") ?? @"C:\Program Files\nodejs\node.exe"
            : _config.NodeExe;
        var bin = Path.Combine(_config.RepoRoot, "apps", "cli", "src", "bin.ts");
        if (!File.Exists(node))
        {
            throw new InvalidOperationException($"找不到 Node：{node}");
        }
        if (!File.Exists(bin))
        {
            throw new InvalidOperationException($"找不到仓库启动入口：{bin}");
        }

        Directory.CreateDirectory(DataDir);
        var log = new StringBuilder();
        SetStatus("正在启动仓库里的 dsh web…");

        var psi = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = _config.RepoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--import");
        psi.ArgumentList.Add("tsx/esm");
        psi.ArgumentList.Add(bin);
        // `web` is the profile alias; --profile is a parent flag and is rejected after it.
        psi.ArgumentList.Add("web");
        psi.ArgumentList.Add("--no-open");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(_config.Port.ToString());
        var path = psi.Environment["PATH"] ?? "";
        var nodeDir = Path.GetDirectoryName(node);
        if (!string.IsNullOrEmpty(nodeDir) && !path.Contains(nodeDir, StringComparison.OrdinalIgnoreCase))
        {
            psi.Environment["PATH"] = nodeDir + ";" + path;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void onLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var clean = Ansi.Replace(line, "").Trim();
            lock (log)
            {
                log.AppendLine(clean);
            }
            var match = ReadyUrl.Match(clean);
            if (match.Success)
            {
                ready.TrySetResult(match.Value.Trim().TrimEnd(')', ',', ';'));
            }
        }
        process.OutputDataReceived += (_, e) => onLine(e.Data);
        process.ErrorDataReceived += (_, e) => onLine(e.Data);
        process.Exited += (_, _) =>
        {
            if (ready.Task.IsCompleted) return;
            string dump;
            lock (log) dump = Tail(log.ToString(), 40);
            File.WriteAllText(LogPath, dump);
            ready.TrySetException(new InvalidOperationException(
                $"dsh web 进程提前退出（code {process.ExitCode}）。\n{dump}"));
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 pnpm dsh web（node 进程没起来）");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        cts.Token.Register(() => ready.TrySetCanceled(cts.Token));
        try
        {
            var url = await ready.Task.WaitAsync(cts.Token);
            new LaunchState { Pid = process.Id, AuthenticatedUrl = url }.Save();
            lock (log) File.WriteAllText(LogPath, log.ToString());
            return url;
        }
        catch (OperationCanceledException)
        {
            string dump;
            lock (log) dump = Tail(log.ToString(), 40);
            File.WriteAllText(LogPath, dump);
            throw new TimeoutException(
                "90 秒内没有等到 dsh web 的 token URL。\n" + dump);
        }
    }

    static string Tail(string text, int lines)
    {
        var all = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
    }

    static async Task<bool> PortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit || e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
        _tray.ShowBalloonTip(1500, "DeepSeek Harness", "已收起到托盘。右键图标可退出。", ToolTipIcon.Info);
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void Quit()
    {
        _reallyExit = true;
        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _icon.Dispose();
            _web.Dispose();
            _status.Dispose();
        }
        base.Dispose(disposing);
    }
}
