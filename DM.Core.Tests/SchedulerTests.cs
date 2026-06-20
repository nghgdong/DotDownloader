using System.Diagnostics;
using DM.Core.Models;
using DM.Core.Queue;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class SchedulerTests
{
    private sealed class TimestampRunner : IDownloadRunner
    {
        public readonly TaskCompletionSource<TimeSpan> Started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Stopwatch _clock;

        public TimestampRunner(Stopwatch clock) => _clock = clock;

        public Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        {
            Started.TrySetResult(_clock.Elapsed);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Scheduled_Task_Starts_Around_Its_Time()
    {
        var clock = Stopwatch.StartNew();
        var runner = new TimestampRunner(clock);
        using var queue = new DownloadQueue(runner, maxConcurrent: 2);
        using var scheduler = new Scheduler(queue, interval: TimeSpan.FromMilliseconds(250));

        var delay = TimeSpan.FromSeconds(5);
        var task = new DownloadTask
        {
            Url = "https://example.com/scheduled",
            FilePath = "s.bin",
            ScheduledAt = DateTimeOffset.Now + delay
        };
        scheduler.Schedule(task);

        var startedAt = await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Bắt đầu quanh mốc +5s (cho phép trễ do tick 250ms + jitter).
        startedAt.Should().BeGreaterThanOrEqualTo(delay - TimeSpan.FromMilliseconds(300));
        startedAt.Should().BeLessThan(delay + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Tick_Enqueues_Only_Due_Tasks()
    {
        var clock = Stopwatch.StartNew();
        var runner = new TimestampRunner(clock);
        using var queue = new DownloadQueue(runner, maxConcurrent: 2);
        // Interval rất dài → chỉ chạy khi gọi Tick() thủ công.
        using var scheduler = new Scheduler(queue, interval: TimeSpan.FromMinutes(10));

        var future = new DownloadTask
        {
            Url = "future", FilePath = "f", ScheduledAt = DateTimeOffset.Now.AddMinutes(30)
        };
        var dueNow = new DownloadTask
        {
            Url = "due", FilePath = "d", ScheduledAt = DateTimeOffset.Now.AddSeconds(-1)
        };
        scheduler.Schedule(future);
        scheduler.Schedule(dueNow);

        scheduler.Tick();

        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); // dueNow đã chạy
        queue.Tasks.Should().ContainSingle().Which.Url.Should().Be("due");
        future.State.Should().Be(DownloadState.Queued); // vẫn chờ, chưa vào queue
    }
}
