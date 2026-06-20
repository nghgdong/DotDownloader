using DM.Core.Models;
using DM.Core.Queue;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class DownloadQueueTests
{
    /// <summary>Runner giả: đếm số tải đồng thời, ghi max quan sát được.</summary>
    private sealed class CountingRunner : IDownloadRunner
    {
        private int _current;
        public int MaxConcurrent;
        public int Completed;
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(200);

        public async Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        {
            int now = Interlocked.Increment(ref _current);
            InterlockedMax(ref MaxConcurrent, now);
            try
            {
                await Task.Delay(Delay, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
            Interlocked.Increment(ref Completed);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            do { seen = Volatile.Read(ref target); if (value <= seen) return; }
            while (Interlocked.CompareExchange(ref target, value, seen) != seen);
        }
    }

    private static DownloadTask NewTask(int i) =>
        new() { Url = $"https://example.com/{i}", FilePath = $"f{i}.bin" };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new TimeoutException("Điều kiện không đạt trong thời gian chờ.");
            }
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task At_Most_MaxConcurrent_Run_Simultaneously()
    {
        var runner = new CountingRunner { Delay = TimeSpan.FromMilliseconds(150) };
        using var queue = new DownloadQueue(runner, maxConcurrent: 3);

        for (int i = 0; i < 10; i++)
        {
            queue.Add(NewTask(i));
        }

        await WaitUntilAsync(() => runner.Completed == 10);

        runner.MaxConcurrent.Should().BeLessThanOrEqualTo(3);
        runner.MaxConcurrent.Should().Be(3); // có lúc đạt đúng giới hạn
        queue.Tasks.Should().OnlyContain(t => t.State == DownloadState.Completed);
    }

    [Fact]
    public async Task PauseAll_Then_ResumeAll_Completes_All()
    {
        var runner = new CountingRunner { Delay = TimeSpan.FromMilliseconds(300) };
        using var queue = new DownloadQueue(runner, maxConcurrent: 2);

        for (int i = 0; i < 6; i++)
        {
            queue.Add(NewTask(i));
        }

        await WaitUntilAsync(() => queue.RunningCount > 0);
        queue.PauseAll();
        // Chờ tới khi mọi worker (đang chạy & đang chờ slot) đều settle về Paused.
        await WaitUntilAsync(() => queue.Tasks.All(t => t.State == DownloadState.Paused));
        queue.RunningCount.Should().Be(0);

        queue.ResumeAll();
        await WaitUntilAsync(() => queue.Tasks.All(t => t.State == DownloadState.Completed), 15000);
    }

    [Fact]
    public async Task AfterQueue_Action_Fires_When_Drained()
    {
        var runner = new CountingRunner { Delay = TimeSpan.FromMilliseconds(50) };
        var power = new SpyPower();
        using var queue = new DownloadQueue(runner, maxConcurrent: 3, power)
        {
            AfterQueueAction = AfterQueueAction.Shutdown
        };

        for (int i = 0; i < 4; i++)
        {
            queue.Add(NewTask(i));
        }

        await WaitUntilAsync(() => power.ShutdownCalls > 0);
        power.ShutdownCalls.Should().Be(1);
    }

    [Fact]
    public async Task Failed_Task_Does_Not_Block_Queue()
    {
        var runner = new FlakyRunner();
        using var queue = new DownloadQueue(runner, maxConcurrent: 2);

        var bad = queue.Add(new DownloadTask { Url = "fail", FilePath = "x" });
        for (int i = 0; i < 3; i++)
        {
            queue.Add(NewTask(i));
        }

        await WaitUntilAsync(() => queue.IsIdle);
        bad.State.Should().Be(DownloadState.Failed);
        queue.Tasks.Count(t => t.State == DownloadState.Completed).Should().Be(3);
    }

    private sealed class SpyPower : ISystemPowerController
    {
        public int ShutdownCalls;
        public int SleepCalls;
        public void Shutdown() => Interlocked.Increment(ref ShutdownCalls);
        public void Sleep() => Interlocked.Increment(ref SleepCalls);
    }

    private sealed class FlakyRunner : IDownloadRunner
    {
        public async Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        {
            await Task.Delay(30, ct);
            if (task.Url == "fail")
            {
                throw new InvalidOperationException("boom");
            }
        }
    }
}
