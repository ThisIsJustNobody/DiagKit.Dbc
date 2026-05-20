namespace DiagKit.Dbc;

/// <summary>
/// DBC 信号值的编码类型 / DBC signal value encoding type.
/// </summary>
public enum DbcSignalValueType
{
    /// <summary>
    /// 有符号整数 / Signed integer.
    /// </summary>
    Signed,

    /// <summary>
    /// 无符号整数 / Unsigned integer.
    /// </summary>
    Unsigned,

    /// <summary>
    /// IEEE 754 32-bit 浮点数 / IEEE 754 32-bit float.
    /// </summary>
    Float,

    /// <summary>
    /// IEEE 754 64-bit 浮点数 / IEEE 754 64-bit double.
    /// </summary>
    Double,
}
