using System.Windows;
using DM.App.Services;
using DM.App.ViewModels;
using DM.Core.Download;
using DM.Core.Net;
using DM.Core.Queue;
using DM.Core.Torrent;
using DM.Core.Util;
using DM.Server;
using DM.Server.Security;

namespace DM.App;

public partial class App : System.Windows.Application
{
    private AppSettings _settings = new();
    private DownloadQueue? _queue;
    private MainViewModel? _viewModel;
    private LocalServer? _server;
    private TorrentDownloader? _torrent;
    private System.Windows.Threading.DispatcherTimer? _keepAwakeTimer;

    public string? ServerToken { get; private set; }
    public int ServerPort { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();

        // Engine + queue + persistence + VM. RateLimiter toàn cục từ cấu hình.
        var rateLimiter = new RateLimiter(_settings.SpeedLimitBytesPerSec);
        var engine = new DownloadEngine(rateLimiter: rateLimiter);
        var httpRunner = new EngineDownloadRunner(engine, _settings.SegmentCount);
        _torrent = new TorrentDownloader(seedAfterComplete: false);
        var runner = new RoutingDownloadRunner(httpRunner, _torrent); // HTTP + Torrent
        _queue = new DownloadQueue(runner, _settings.MaxConcurrent);
        var store = new TaskStore();
        _viewModel = new MainViewModel(_queue, _settings, store, new HttpProbe(), Dispatcher);

        // Local server: enqueue từ extension → VM.
        try
        {
            ServerToken = new TokenProvider().GetOrCreateToken();
            _server = new LocalServer(ServerToken, _viewModel.EnqueueFromRequest, () => _queue.RunningCount);
            await _server.StartAsync(_settings.Port);
            ServerPort = _server.Port;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không khởi động được local server: {ex.Message}",
                "DotDownloader", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var window = new MainWindow(_viewModel, _settings);
        MainWindow = window;
        window.Show();

        // Chống Windows ngủ khi đang tải (cho tải dài 24/7). Kiểm tra mỗi 15s.
        _keepAwakeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _keepAwakeTimer.Tick += (_, _) => KeepAwake.Set(_queue is { RunningCount: > 0 });
        _keepAwakeTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keepAwakeTimer?.Stop();
        KeepAwake.Set(false); // cho phép ngủ lại khi thoát app
        _viewModel?.Persist();
        _server?.StopAsync().GetAwaiter().GetResult();
        _queue?.Dispose();
        _torrent?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
