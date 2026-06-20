using DM.Core;

namespace DM.Server;

/// <summary>
/// Hằng số chung cho local server.
/// </summary>
public static class ServerInfo
{
    public const int DefaultPort = 51820;
    public const int MaxPortAttempts = 10;
    public const string Version = ProjectInfo.Version;
    public const string TokenHeader = "X-DM-Token";
}
