namespace DM.Core.Video;

/// <summary>Một biến thể (chất lượng) trong master playlist.</summary>
public sealed record HlsVariant(long Bandwidth, int? Width, int? Height, string Uri)
{
    public string ResolutionLabel => Width.HasValue && Height.HasValue ? $"{Width}x{Height}" : "?";
}

/// <summary>Khóa mã hóa segment. CHỈ hỗ trợ AES-128 với key công khai trong playlist (KHÔNG DRM).</summary>
public sealed class HlsKey
{
    public required string Method { get; init; } // NONE | AES-128 | SAMPLE-AES(...)
    public string? Uri { get; init; }
    public byte[]? Iv { get; init; }

    public bool IsEncrypted => !string.Equals(Method, "NONE", StringComparison.OrdinalIgnoreCase);
    public bool IsAes128 => string.Equals(Method, "AES-128", StringComparison.OrdinalIgnoreCase);
}

public sealed record HlsSegment(string Uri, double Duration, HlsKey? Key, long MediaSequence);

/// <summary>Kết quả parse một playlist HLS (master hoặc media).</summary>
public sealed class HlsPlaylist
{
    public bool IsMaster { get; init; }
    public List<HlsVariant> Variants { get; init; } = new();
    public List<HlsSegment> Segments { get; init; } = new();

    /// <summary>URI init segment (#EXT-X-MAP) cho fMP4 — cần prepend trước khi ghép.</summary>
    public string? InitSegmentUri { get; init; }

    public double TotalDuration => Segments.Sum(s => s.Duration);
}
