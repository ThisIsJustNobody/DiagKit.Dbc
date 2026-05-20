using System.Diagnostics.CodeAnalysis;

namespace DiagKit.Dbc;

/// <summary>
/// CAN/CAN FD 帧标志位 / CAN/CAN FD frame flags.
/// </summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Flags is the standard .NET suffix for flag enums.")]
public enum DbcFrameFlags
{
    /// <summary>
    /// 无标志 / No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// CAN FD 灵活数据速率帧 / CAN FD flexible data rate frame.
    /// </summary>
    FlexibleDataRate = 1 << 0,

    /// <summary>
    /// 比特率切换 / Bit rate switch.
    /// </summary>
    BitRateSwitch = 1 << 1,

    /// <summary>
    /// 错误状态指示 / Error state indicator.
    /// </summary>
    ErrorStateIndicator = 1 << 2,
}
