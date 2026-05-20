namespace DiagKit.Dbc;

/// <summary>
/// DBC 信号字节序 / DBC signal byte order.
/// </summary>
public enum DbcByteOrder
{
    /// <summary>
    /// Motorola / 大端序 (Big Endian).
    /// </summary>
    Motorola = 0,

    /// <summary>
    /// Intel / 小端序 (Little Endian).
    /// </summary>
    Intel = 1,
}
