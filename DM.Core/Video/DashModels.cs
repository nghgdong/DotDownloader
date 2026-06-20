namespace DM.Core.Video;

/// <summary>Một track (video hoặc audio) đã giải xong init + danh sách segment URL.</summary>
public sealed record DashRepresentation(
    string Id,
    long Bandwidth,
    int? Width,
    int? Height,
    string MimeType,
    string? InitUrl,
    IReadOnlyList<string> SegmentUrls);

/// <summary>Track video + audio tốt nhất chọn từ MPD.</summary>
public sealed class DashManifest
{
    public DashRepresentation? Video { get; init; }
    public DashRepresentation? Audio { get; init; }
}
