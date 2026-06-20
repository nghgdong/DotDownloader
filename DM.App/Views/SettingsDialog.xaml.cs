using System.Globalization;
using System.Windows;
using DM.App.Services;
using DM.Server.Security;

namespace DM.App.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        SegmentBox.Text = settings.SegmentCount.ToString();
        ConcurrentBox.Text = settings.MaxConcurrent.ToString();
        SpeedBox.Text = (settings.SpeedLimitBytesPerSec / 1024).ToString();
        PortBox.Text = settings.Port.ToString();
        DirBox.Text = settings.DownloadDirectory;
        StartupCheck.IsChecked = StartupRegistry.IsEnabled();
        TokenBox.Text = new TokenProvider().GetOrCreateToken();
    }

    private void OnCopyToken(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(TokenBox.Text); } catch { /* clipboard có thể bận */ }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (TryParse(SegmentBox.Text, out var seg) && seg >= 1)
        {
            _settings.SegmentCount = seg;
        }
        if (TryParse(ConcurrentBox.Text, out var conc) && conc >= 1)
        {
            _settings.MaxConcurrent = conc;
        }
        if (TryParse(SpeedBox.Text, out var kb) && kb >= 0)
        {
            _settings.SpeedLimitBytesPerSec = (long)kb * 1024;
        }
        if (TryParse(PortBox.Text, out var port) && port is > 0 and < 65536)
        {
            _settings.Port = port;
        }
        if (!string.IsNullOrWhiteSpace(DirBox.Text))
        {
            _settings.DownloadDirectory = DirBox.Text.Trim();
        }
        StartupRegistry.Set(StartupCheck.IsChecked == true);
        DialogResult = true;
    }

    private static bool TryParse(string s, out int value)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
