namespace DM.Core.Download;

/// <summary>
/// Thực thi một thao tác với retry + exponential backoff. Mặc định: tối đa 5 lần retry,
/// chờ 1/2/4/8/16s giữa các lần. Hủy (CancellationToken) KHÔNG được retry.
/// </summary>
public sealed class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly Func<int, TimeSpan> _backoff;

    /// <param name="maxRetries">Số lần thử lại tối đa sau lần đầu thất bại (mặc định 5).</param>
    /// <param name="backoff">Hàm tính thời gian chờ theo số lần đã retry (0-based). Mặc định 2^n giây.</param>
    public RetryPolicy(int maxRetries = 5, Func<int, TimeSpan>? backoff = null)
    {
        _maxRetries = maxRetries;
        _backoff = backoff ?? (n => TimeSpan.FromSeconds(Math.Pow(2, n))); // 1,2,4,8,16
    }

    /// <summary>Backoff zero — dùng cho test để khỏi chờ thật.</summary>
    public static RetryPolicy NoDelay(int maxRetries = 5) => new(maxRetries, _ => TimeSpan.Zero);

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        int retries = 0;
        while (true)
        {
            try
            {
                await action(ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw; // hủy có chủ đích — không retry
            }
            catch when (retries < _maxRetries)
            {
                var delay = _backoff(retries);
                retries++;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
