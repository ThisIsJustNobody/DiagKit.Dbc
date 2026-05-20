namespace DiagKit.Dbc;

/// <summary>
/// 信号写入策略，控制物理值编码时对越界值的处理方式。<br/>
/// Signal write policy, controlling how out-of-range values are handled during physical value encoding.
/// </summary>
public enum SignalWritePolicy
{
    /// <summary>
    /// 严格模式：越界时返回失败 / Strict: fail on out-of-range.
    /// </summary>
    Strict,

    /// <summary>
    /// 将越界值钳制到 raw range 内 / Clamp out-of-range values to the raw range.
    /// </summary>
    ClampToRawRange,

    /// <summary>
    /// 将越界值钳制到 physical range 内 / Clamp out-of-range values to the physical range.
    /// </summary>
    ClampToPhysicalRange,
}

/// <summary>
/// 信号写入操作的状态码 / Status code for signal write operations.
/// </summary>
public enum SignalWriteStatus
{
    /// <summary>
    /// 未初始化或无效结果 / Uninitialized or invalid result.
    /// </summary>
    Invalid,

    /// <summary>
    /// 写入成功 / Write succeeded.
    /// </summary>
    Success = 1,

    /// <summary>
    /// 信号 factor 为零，无法编码 / Signal factor is zero, cannot encode.
    /// </summary>
    FactorIsZero,

    /// <summary>
    /// 待编码值为非有限值 (NaN/Infinity) / Value is non-finite (NaN/Infinity).
    /// </summary>
    NonFiniteValue,

    /// <summary>
    /// 物理值超出物理范围 / Physical value outside physical range.
    /// </summary>
    OutOfPhysicalRange,

    /// <summary>
    /// 物理值超出 raw range / Physical value outside raw range.
    /// </summary>
    OutOfRawRange,

    /// <summary>
    /// 信号定义无效，如 bit length 不在 1..64 / Invalid signal definition.
    /// </summary>
    InvalidSignalDefinition,
}

/// <summary>
/// 信号写入结果，包含状态码、已写入的 raw/physical 值和可选诊断消息。<br/>
/// Signal write result, containing status, written raw/physical values, and optional diagnostic message.
/// </summary>
public readonly record struct SignalWriteResult(
    SignalWriteStatus Status,
    ulong RawValue,
    double PhysicalValue,
    string? Diagnostic)
{
    /// <summary>
    /// 写入是否成功 / Whether the write succeeded.
    /// </summary>
    public bool Succeeded => Status == SignalWriteStatus.Success;

    /// <summary>
    /// 创建成功的写入结果。<br/>
    /// Creates a successful write result.
    /// </summary>
    public static SignalWriteResult Success(ulong rawValue, double physicalValue)
    {
        return new SignalWriteResult(SignalWriteStatus.Success, rawValue, physicalValue, null);
    }

    /// <summary>
    /// 创建失败的写入结果，附带诊断消息。<br/>
    /// Creates a failed write result with a diagnostic message.
    /// </summary>
    public static SignalWriteResult Fail(SignalWriteStatus status, string diagnostic)
    {
        return new SignalWriteResult(status, 0, double.NaN, diagnostic);
    }
}
