using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DM.Core.Models;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DM.App.Converters;

internal static class StatePalette
{
    public static Color ColorFor(DownloadState s) => (Color)ColorConverter.ConvertFromString(s switch
    {
        DownloadState.Downloading => "#2D7FF9",
        DownloadState.Connecting => "#2D7FF9",
        DownloadState.Completed => "#10B981",
        DownloadState.Paused => "#F59E0B",
        DownloadState.Failed => "#EF4444",
        DownloadState.Canceled => "#6B7280",
        _ => "#9CA3AF" // Queued
    })!;
}

/// <summary>DownloadState → brush đặc (cho chữ/chấm trạng thái).</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(value is DownloadState s ? StatePalette.ColorFor(s) : Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>DownloadState → brush nhạt (nền badge).</summary>
public sealed class StateToLightBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var c = value is DownloadState s ? StatePalette.ColorFor(s) : Colors.Gray;
        return new SolidColorBrush(Color.FromArgb(36, c.R, c.G, c.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>DownloadState → nhãn tiếng Việt.</summary>
public sealed class StateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DownloadState s ? s switch
        {
            DownloadState.Queued => "Trong hàng đợi",
            DownloadState.Connecting => "Đang kết nối",
            DownloadState.Downloading => "Đang tải",
            DownloadState.Paused => "Tạm dừng",
            DownloadState.Completed => "Hoàn tất",
            DownloadState.Failed => "Lỗi",
            DownloadState.Canceled => "Đã hủy",
            _ => s.ToString()
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
