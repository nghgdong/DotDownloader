using System.Runtime.InteropServices;

namespace DM.App.Services;

/// <summary>
/// Ngăn Windows tự ngủ (sleep) trong khi đang tải, để tải dài 24/7 không bị gián đoạn.
/// KHÔNG giữ màn hình sáng — chỉ giữ hệ thống thức. Phải gọi trên một thread bền (UI thread).
/// </summary>
public static class KeepAwake
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(ExecutionState esFlags);

    private static bool _active;

    /// <summary>Bật/tắt chống ngủ. <paramref name="prevent"/> = true khi có download đang chạy.</summary>
    public static void Set(bool prevent)
    {
        if (prevent == _active)
        {
            return; // không gọi lại thừa
        }
        SetThreadExecutionState(prevent
            ? ExecutionState.Continuous | ExecutionState.SystemRequired
            : ExecutionState.Continuous);
        _active = prevent;
    }
}
