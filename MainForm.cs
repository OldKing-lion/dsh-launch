using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
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
    static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dsh-repo-shell",
        "webview");

    readonly AppConfig _config = AppConfig.Load();
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
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

        Controls.Add(_web);

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

    async Task BootAsync()
    {
        try
        {
            Directory.CreateDirectory(UserDataDir);
            var env = await CoreWebView2Environment.CreateAsync(null, UserDataDir);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var url = await EnsureDshUrlAsync();
            _web.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "DeepSeek Harness 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    async Task<string> EnsureDshUrlAsync()
    {
        if (await PortOpenAsync(_config.Port))
        {
            return $"http://127.0.0.1:{_config.Port}/";
        }

        var node = string.IsNullOrWhiteSpace(_config.NodeExe)
            ? Environment.GetEnvironmentVariable("DSH_NODE") ?? @"C:\Program Files\nodejs\node.exe"
            : _config.NodeExe;
        var bin = Path.Combine(_config.RepoRoot, "apps", "cli", "src", "bin.ts");
        if (!File.Exists(bin))
        {
            throw new InvalidOperationException($"找不到仓库启动入口：{bin}");
        }

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
        psi.ArgumentList.Add("web");
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add(_config.Profile);
        psi.ArgumentList.Add("--no-open");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(_config.Port.ToString());

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void onLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var match = System.Text.RegularExpressions.Regex.Match(
                line,
                @"dsh web:\s+(http://127\.0\.0\.1:\d+/\S*)");
            if (match.Success)
            {
                ready.TrySetResult(match.Groups[1].Value.Trim());
            }
        }
        process.OutputDataReceived += (_, e) => onLine(e.Data);
        process.ErrorDataReceived += (_, e) => onLine(e.Data);
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
            return await ready.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                "90 秒内没有等到 dsh web 就绪。请先在仓库目录手动运行 pnpm dsh web --no-open 看报错。");
        }
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
        }
        base.Dispose(disposing);
    }
}
