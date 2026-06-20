using System.Diagnostics;

namespace DM.Core.Util;

/// <summary>
/// Token bucket giới hạn tốc độ tải TOÀN CỤC (byte/giây). Chia sẻ giữa nhiều segment/download.
/// <c>bytesPerSecond &lt;= 0</c> → không giới hạn. Thread-safe.
/// </summary>
public sealed class RateLimiter
{
    private const long DefaultBurst = 1 * 1024 * 1024; // 1MB

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _ratePerSecond;
    private long _burst;
    private double _available;
    private TimeSpan _lastRefill;

    public RateLimiter(long bytesPerSecond = 0, long? burstBytes = null)
        => Configure(bytesPerSecond, burstBytes);

    public long BytesPerSecond => Interlocked.Read(ref _ratePerSecond);

    public void SetRate(long bytesPerSecond, long? burstBytes = null)
    {
        lock (_gate)
        {
            Configure(bytesPerSecond, burstBytes);
        }
    }

    private void Configure(long bytesPerSecond, long? burstBytes)
    {
        _ratePerSecond = Math.Max(0, bytesPerSecond);
        // Burst phải >= 1 lần đọc để một chunk luôn có thể "lọt" qua.
        _burst = burstBytes ?? Math.Max(_ratePerSecond, DefaultBurst);
        if (_burst < 1)
        {
            _burst = DefaultBurst;
        }
        _available = Math.Min(_available, _burst);
        _lastRefill = _clock.Elapsed;
    }

    /// <summary>Chờ tới khi đủ "quota" cho <paramref name="bytes"/> rồi tiêu thụ. Không giới hạn → trả ngay.</summary>
    public async Task ThrottleAsync(long bytes, CancellationToken ct = default)
    {
        if (bytes <= 0)
        {
            return;
        }

        while (true)
        {
            int waitMs;
            lock (_gate)
            {
                if (_ratePerSecond <= 0)
                {
                    return; // unlimited
                }
                Refill();
                if (_available >= bytes)
                {
                    _available -= bytes;
                    return;
                }
                double deficit = bytes - _available;
                double seconds = deficit / _ratePerSecond;
                waitMs = (int)Math.Clamp(Math.Ceiling(seconds * 1000), 1, 1000);
            }
            await Task.Delay(waitMs, ct).ConfigureAwait(false);
        }
    }

    private void Refill()
    {
        var now = _clock.Elapsed;
        var elapsed = (now - _lastRefill).TotalSeconds;
        if (elapsed > 0)
        {
            _available = Math.Min(_burst, _available + elapsed * _ratePerSecond);
            _lastRefill = now;
        }
    }
}
