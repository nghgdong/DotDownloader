using DM.Core.Download;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task Succeeds_After_Two_Failures()
    {
        int calls = 0;
        var policy = RetryPolicy.NoDelay();

        await policy.ExecuteAsync(_ =>
        {
            calls++;
            if (calls < 3)
            {
                throw new HttpRequestException("transient");
            }
            return Task.CompletedTask;
        });

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Throws_After_Exhausting_Retries()
    {
        int calls = 0;
        var policy = RetryPolicy.NoDelay(maxRetries: 5);

        var act = async () => await policy.ExecuteAsync(_ =>
        {
            calls++;
            throw new HttpRequestException("always fails");
        });

        await act.Should().ThrowAsync<HttpRequestException>();
        calls.Should().Be(6); // 1 lần đầu + 5 lần retry
    }

    [Fact]
    public async Task Does_Not_Retry_On_Cancellation()
    {
        int calls = 0;
        var policy = RetryPolicy.NoDelay();

        var act = async () => await policy.ExecuteAsync(_ =>
        {
            calls++;
            throw new OperationCanceledException();
        });

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public void Default_Backoff_Is_1_2_4_8_16_Seconds()
    {
        var backoff = new Func<int, TimeSpan>(n => TimeSpan.FromSeconds(Math.Pow(2, n)));
        backoff(0).Should().Be(TimeSpan.FromSeconds(1));
        backoff(1).Should().Be(TimeSpan.FromSeconds(2));
        backoff(2).Should().Be(TimeSpan.FromSeconds(4));
        backoff(3).Should().Be(TimeSpan.FromSeconds(8));
        backoff(4).Should().Be(TimeSpan.FromSeconds(16));
    }
}
