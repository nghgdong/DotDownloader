using System.Globalization;

namespace DM.Core.Video;

/// <summary>
/// Parser playlist HLS (.m3u8). Phân biệt master (có #EXT-X-STREAM-INF) và media playlist
/// (có #EXTINF), tuyệt đối hóa URI theo base, đọc #EXT-X-KEY (AES-128) &amp; #EXT-X-MAP.
/// </summary>
public static class M3u8Parser
{
    public static HlsPlaylist Parse(string content, Uri baseUri)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        bool isMaster = lines.Any(l => l.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal));
        return isMaster ? ParseMaster(lines, baseUri) : ParseMedia(lines, baseUri);
    }

    private static HlsPlaylist ParseMaster(string[] lines, Uri baseUri)
    {
        var variants = new List<HlsVariant>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal))
            {
                continue;
            }

            var attrs = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
            long bandwidth = attrs.TryGetValue("BANDWIDTH", out var bw)
                ? long.Parse(bw, CultureInfo.InvariantCulture) : 0;
            int? width = null, height = null;
            if (attrs.TryGetValue("RESOLUTION", out var res))
            {
                var wh = res.Split('x', 'X');
                if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h))
                {
                    width = w;
                    height = h;
                }
            }

            // URI nằm ở dòng không-comment kế tiếp.
            string? uri = NextUri(lines, ref i);
            if (uri is not null)
            {
                variants.Add(new HlsVariant(bandwidth, width, height, Absolutize(uri, baseUri)));
            }
        }
        return new HlsPlaylist { IsMaster = true, Variants = variants };
    }

    private static HlsPlaylist ParseMedia(string[] lines, Uri baseUri)
    {
        var segments = new List<HlsSegment>();
        HlsKey? currentKey = null;
        string? initUri = null;
        long mediaSequence = 0;
        long seq = 0;
        double pendingDuration = 0;
        bool seqInitialized = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
            {
                mediaSequence = long.Parse(line[(line.IndexOf(':') + 1)..], CultureInfo.InvariantCulture);
                seq = mediaSequence;
                seqInitialized = true;
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
            {
                currentKey = ParseKey(line[(line.IndexOf(':') + 1)..], baseUri);
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var attrs = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                if (attrs.TryGetValue("URI", out var mu))
                {
                    initUri = Absolutize(mu, baseUri);
                }
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var durStr = line[(line.IndexOf(':') + 1)..].Split(',')[0];
                double.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out pendingDuration);
            }
            else if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                if (!seqInitialized)
                {
                    seqInitialized = true; // mặc định bắt đầu từ 0
                }
                var key = (currentKey is not null && currentKey.IsEncrypted) ? currentKey : null;
                segments.Add(new HlsSegment(Absolutize(line, baseUri), pendingDuration, key, seq));
                seq++;
                pendingDuration = 0;
            }
        }

        return new HlsPlaylist { IsMaster = false, Segments = segments, InitSegmentUri = initUri };
    }

    private static HlsKey ParseKey(string attrText, Uri baseUri)
    {
        var attrs = ParseAttributes(attrText);
        var method = attrs.TryGetValue("METHOD", out var m) ? m : "NONE";
        string? uri = attrs.TryGetValue("URI", out var u) ? Absolutize(u, baseUri) : null;
        byte[]? iv = null;
        if (attrs.TryGetValue("IV", out var ivStr))
        {
            iv = HexToBytes(ivStr);
        }
        return new HlsKey { Method = method, Uri = uri, Iv = iv };
    }

    private static string? NextUri(string[] lines, ref int i)
    {
        for (int j = i + 1; j < lines.Length; j++)
        {
            var l = lines[j].Trim();
            if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            i = j;
            return l;
        }
        return null;
    }

    /// <summary>Parse danh sách thuộc tính KEY=VALUE,KEY="V,V" tôn trọng dấu nháy kép.</summary>
    internal static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < text.Length)
        {
            int eq = text.IndexOf('=', i);
            if (eq < 0)
            {
                break;
            }
            var key = text[i..eq].Trim();
            i = eq + 1;
            string value;
            if (i < text.Length && text[i] == '"')
            {
                int end = text.IndexOf('"', i + 1);
                if (end < 0)
                {
                    end = text.Length;
                }
                value = text[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                int comma = text.IndexOf(',', i);
                if (comma < 0)
                {
                    comma = text.Length;
                }
                value = text[i..comma].Trim();
                i = comma;
            }
            if (i < text.Length && text[i] == ',')
            {
                i++;
            }
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }
        return result;
    }

    internal static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || hex.StartsWith("0X", StringComparison.Ordinal))
        {
            hex = hex[2..];
        }
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static string Absolutize(string uri, Uri baseUri)
        => Uri.TryCreate(baseUri, uri, out var abs) ? abs.ToString() : uri;
}
