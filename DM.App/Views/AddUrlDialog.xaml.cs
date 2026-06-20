using System.IO;
using System.Linq;
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

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file .torrent",
            Filter = "Torrent (*.torrent)|*.torrent|Tất cả (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            SetTorrentFile(dialog.FileName);
        }
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var torrent = files.FirstOrDefault(f => f.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                          ?? files[0];
            SetTorrentFile(torrent);
        }
    }

    private void SetTorrentFile(string path)
    {
        UrlBox.Text = path;
        if (string.IsNullOrWhiteSpace(FileNameBox.Text))
        {
            FileNameBox.Text = path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(path)
                : Path.GetFileName(path);
        }
        InfoText.Text = "Đã chọn .torrent — bấm Tải để bắt đầu (dung lượng biết sau khi lấy metadata).";
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
