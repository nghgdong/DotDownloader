namespace DM.Core.Queue;

/// <summary>Hành động sau khi tải xong toàn bộ hàng đợi.</summary>
public enum AfterQueueAction
{
    None,
    Shutdown,
    Sleep
}

/// <summary>Điều khiển nguồn hệ thống (tắt máy / ngủ). Tách interface để test không tắt máy thật.</summary>
public interface ISystemPowerController
{
    void Shutdown();
    void Sleep();
}

/// <summary>Mặc định: không làm gì (an toàn cho test &amp; môi trường không hỗ trợ).</summary>
public sealed class NoOpPowerController : ISystemPowerController
{
    public void Shutdown() { }
    public void Sleep() { }
}
