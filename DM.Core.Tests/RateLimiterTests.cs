using System.Diagnostics;
using DM.Core.Util;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task Unlimited_Does_Not_Delay()
    {
        var limiter = new RateLimiter(0);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            await limiter.ThrottleAsync(1_000_000_000); // 1GB mỗi lần — nếu có throttle sẽ chờ rất lâu
        }
        // Rate 0 = không giới hạn → trả ngay, không phụ thuộc số byte. Nới ngưỡng vì test chạy song song.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Limited_Rate_Enforces_Delay()
    {
        // 100 KB/s, burst nhỏ 10 KB → tiêu 60 KB phải mất ~ (60-10)/100 = 0.5s.
        var limiter = new RateLimiter(100_000, burstBytes: 10_000);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 6; i++)
        {
            await limiter.ThrottleAsync(10_000);
        }
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task SetRate_To_Unlimited_Stops_Throttling()
    {
        var limiter = new RateLimiter(10_000, burstBytes: 1000);
        limiter.SetRate(0);
        var sw = Stopwatch.StartNew();
        await limiter.ThrottleAsync(10_000_000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }
}
