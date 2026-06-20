using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DM.Core.Video;

/// <summary>
/// Parser DASH manifest (.mpd) cho dạng phổ biến nhất: <c>SegmentTemplate</c> với
/// <c>$Number$</c> (kèm @duration HOẶC SegmentTimeline). Chọn 1 track video + 1 audio
/// bitrate cao nhất. KHÔNG hỗ trợ SegmentList / SegmentBase byte-range / multi-period.
/// </summary>
public static class MpdParser
{
    public static DashManifest Parse(string xml, Uri baseUri)
    {
        var doc = XDocument.Parse(xml);
        var mpd = doc.Root ?? throw new InvalidOperationException("MPD rỗng.");

        double totalSeconds = ParseDuration(Attr(mpd, "mediaPresentationDuration"));

        var current = CombineBase(baseUri, ChildBaseUrl(mpd));

        var period = Children(mpd, "Period").FirstOrDefault()
            ?? throw new InvalidOperationException("MPD không có Period.");
        current = CombineBase(current, ChildBaseUrl(period));
        if (totalSeconds <= 0)
        {
            totalSeconds = ParseDuration(Attr(period, "duration"));
        }

        DashRepresentation? video = null, audio = null;

        foreach (var aset in Children(period, "AdaptationSet"))
        {
            var asetBase = CombineBase(current, ChildBaseUrl(aset));
            var kind = AdaptationKind(aset);
            if (kind is null)
            {
                continue;
            }

            var reps = Children(aset, "Representation").ToList();
            if (reps.Count == 0)
            {
                continue;
            }
            var best = reps.OrderByDescending(r => ParseLong(Attr(r, "bandwidth"))).First();
            var rep = BuildRepresentation(aset, best, asetBase, totalSeconds, kind);
            if (rep is null)
            {
                continue;
            }

            if (kind == "video" && video is null)
            {
                video = rep;
            }
            else if (kind == "audio" && audio is null)
            {
                audio = rep;
            }
        }

        return new DashManifest { Video = video, Audio = audio };
    }

    private static DashRepresentation? BuildRepresentation(
        XElement aset, XElement rep, Uri baseUri, double totalSeconds, string kind)
    {
        var template = Child(rep, "SegmentTemplate") ?? Child(aset, "SegmentTemplate");
        if (template is null)
        {
            return null; // chỉ hỗ trợ SegmentTemplate
        }

        string id = Attr(rep, "id") ?? "0";
        long bandwidth = ParseLong(Attr(rep, "bandwidth"));
        int? width = ParseIntNullable(Attr(rep, "width") ?? Attr(aset, "width"));
        int? height = ParseIntNullable(Attr(rep, "height") ?? Attr(aset, "height"));
        string mime = Attr(rep, "mimeType") ?? Attr(aset, "mimeType") ?? (kind == "video" ? "video/mp4" : "audio/mp4");

        long timescale = ParseLong(Attr(template, "timescale"));
        if (timescale <= 0)
        {
            timescale = 1;
        }
        long startNumber = ParseLong(Attr(template, "startNumber"));
        if (startNumber <= 0)
        {
            startNumber = 1;
        }

        string? initTemplate = Attr(template, "initialization");
        string? mediaTemplate = Attr(template, "media");
        if (mediaTemplate is null)
        {
            return null;
        }

        string? initUrl = initTemplate is null
            ? null
            : Absolutize(Substitute(initTemplate, id, bandwidth, 0, 0), baseUri);

        var segmentUrls = new List<string>();
        var timeline = Child(template, "SegmentTimeline");
        if (timeline is not null)
        {
            long number = startNumber;
            long time = 0;
            bool firstT = true;
            foreach (var s in Children(timeline, "S"))
            {
                long t = ParseLong(Attr(s, "t"));
                if (Attr(s, "t") is not null)
                {
                    time = t;
                }
                else if (firstT)
                {
                    time = 0;
                }
                firstT = false;
                long d = ParseLong(Attr(s, "d"));
                long r = ParseLong(Attr(s, "r")); // số lần lặp THÊM
                for (long k = 0; k <= r; k++)
                {
                    segmentUrls.Add(Absolutize(Substitute(mediaTemplate, id, bandwidth, number, time), baseUri));
                    number++;
                    time += d;
                }
            }
        }
        else
        {
            long duration = ParseLong(Attr(template, "duration"));
            if (duration <= 0 || totalSeconds <= 0)
            {
                return null; // không suy ra được số segment
            }
            double segSeconds = (double)duration / timescale;
            int count = (int)Math.Ceiling(totalSeconds / segSeconds);
            long time = 0;
            for (int i = 0; i < count; i++)
            {
                long number = startNumber + i;
                segmentUrls.Add(Absolutize(Substitute(mediaTemplate, id, bandwidth, number, time), baseUri));
                time += duration;
            }
        }

        return new DashRepresentation(id, bandwidth, width, height, mime, initUrl, segmentUrls);
    }

    // ---------- helpers ----------

    private static string? AdaptationKind(XElement aset)
    {
        string Probe(string? s) => s ?? "";
        var ct = Probe(Attr(aset, "contentType"));
        var mime = Probe(Attr(aset, "mimeType"));
        var repMime = Probe(Children(aset, "Representation").FirstOrDefault() is { } r ? Attr(r, "mimeType") : null);
        var all = $"{ct} {mime} {repMime}".ToLowerInvariant();
        if (all.Contains("video"))
        {
            return "video";
        }
        if (all.Contains("audio"))
        {
            return "audio";
        }
        return null;
    }

    private static string Substitute(string template, string id, long bandwidth, long number, long time)
    {
        var s = template
            .Replace("$RepresentationID$", id, StringComparison.Ordinal)
            .Replace("$Bandwidth$", bandwidth.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("$$", "$", StringComparison.Ordinal);

        s = ReplaceFormatted(s, "Number", number);
        s = ReplaceFormatted(s, "Time", time);
        return s;
    }

    /// <summary>Thay $Name$ và $Name%0Nd$ bằng giá trị (hỗ trợ định dạng width như %05d).</summary>
    private static string ReplaceFormatted(string input, string name, long value)
    {
        var rx = new Regex(@"\$" + name + @"(%0(\d+)d)?\$");
        return rx.Replace(input, m =>
        {
            if (m.Groups[2].Success)
            {
                int width = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            }
            return value.ToString(CultureInfo.InvariantCulture);
        });
    }

    private static string? ChildBaseUrl(XElement el)
        => Children(el, "BaseURL").FirstOrDefault()?.Value?.Trim();

    private static Uri CombineBase(Uri current, string? relative)
        => string.IsNullOrEmpty(relative) ? current
            : (Uri.TryCreate(current, relative, out var u) ? u : current);

    private static string Absolutize(string uri, Uri baseUri)
        => Uri.TryCreate(baseUri, uri, out var abs) ? abs.ToString() : uri;

    private static IEnumerable<XElement> Children(XElement parent, string localName)
        => parent.Elements().Where(e => e.Name.LocalName == localName);

    private static XElement? Child(XElement parent, string localName)
        => Children(parent, localName).FirstOrDefault();

    private static string? Attr(XElement el, string name)
        => el.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private static long ParseLong(string? s)
        => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int? ParseIntNullable(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Parse ISO-8601 duration (vd PT1H2M3.5S) → giây.</summary>
    internal static double ParseDuration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return 0;
        }
        var m = Regex.Match(iso,
            @"^P(?:(?<d>\d+(?:\.\d+)?)D)?(?:T(?:(?<h>\d+(?:\.\d+)?)H)?(?:(?<m>\d+(?:\.\d+)?)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$");
        if (!m.Success)
        {
            return 0;
        }
        double Get(string g) => m.Groups[g].Success
            ? double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        return Get("d") * 86400 + Get("h") * 3600 + Get("m") * 60 + Get("s");
    }
}
