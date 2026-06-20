using System.ComponentModel;
using System.Windows;
using DM.App.Services;
using DM.App.ViewModels;
using DM.App.Views;

namespace DM.App;

public partial class MainWindow : Window
{
    private readonly AppSettings? _settings;
    private System.Windows.Forms.NotifyIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();
        SetupTray();
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    public MainWindow(MainViewModel viewModel, AppSettings settings) : this()
    {
        DataContext = viewModel;
        _settings = settings;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dialog = new AddUrlDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Vm.AddDownload(dialog.Url, dialog.FileName, dialog.TotalBytes, dialog.SupportsRange);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var dialog = new SettingsDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Save();
        }
    }

    // ---------- Tray / minimize to tray ----------

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "DotDownloader"
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Mở", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Thoát", null, (_, _) =>
        {
            if (_tray is not null) _tray.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        _tray.ContextMenuStrip = menu;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // minimize to tray
            _tray?.ShowBalloonTip(1000, "DotDownloader", "Đang chạy nền trong khay hệ thống.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
