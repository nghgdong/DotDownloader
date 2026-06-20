using System.Windows;
using DM.Core.Net;

namespace DM.App.Views;

public partial class AddUrlDialog : Window
{
    private readonly HttpProbe _probe = new();

    public string Url { get; private set; } = "";
    public string? FileName { get; private set; }
    public long TotalBytes { get; private set; } = -1;
    public bool SupportsRange { get; private set; }

    public AddUrlDialog()
    {
        InitializeComponent();
        UrlBox.Focus();
    }

    private async void OnProbeClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            || url.Split('?', '#')[0].EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            InfoText.Text = "Torrent: dung lượng sẽ biết sau khi lấy metadata. Bấm Tải để bắt đầu.";
            return;
        }
        ProbeButton.IsEnabled = false;
        InfoText.Text = "Đang phân tích…";
        try
        {
            var p = await _probe.ProbeAsync(url);
            TotalBytes = p.TotalBytes;
            SupportsRange = p.SupportsRange;
            if (string.IsNullOrWhiteSpace(FileNameBox.Text) && !string.IsNullOrWhiteSpace(p.SuggestedFileName))
            {
                FileNameBox.Text = p.SuggestedFileName;
            }
            var size = p.TotalBytes >= 0 ? FormatBytes(p.TotalBytes) : "không rõ";
            InfoText.Text = $"Kích thước: {size} · Hỗ trợ tải đa luồng/resume: {(p.SupportsRange ? "Có" : "Không")}";
        }
        catch (Exception ex)
        {
            InfoText.Text = $"Không phân tích được: {ex.Message}";
        }
        finally
        {
            ProbeButton.IsEnabled = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            InfoText.Text = "URL không hợp lệ.";
            return;
        }
        Url = url;
        FileName = string.IsNullOrWhiteSpace(FileNameBox.Text) ? null : FileNameBox.Text.Trim();
        DialogResult = true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
