namespace DM.Core.Net;

/// <summary>
/// Một <see cref="HttpClient"/> dùng chung toàn ứng dụng. KHÔNG tạo HttpClient mới
/// cho mỗi request (tránh cạn socket). Dùng làm mặc định khi không inject client riêng.
/// </summary>
public static class SharedHttpClient
{
    public static HttpClient Instance { get; } = CreateDefault();

    private static HttpClient CreateDefault()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        // Probe/tải file lớn: không đặt timeout tổng cho cả client; điều phối bằng CancellationToken.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
