namespace DiagKit.Dbc;

/// <summary>
/// DBC 写出模式。<br/>
/// DBC write mode.
/// </summary>
public enum DbcWriteMode
{
    /// <summary>
    /// 严格模式：可能导致 reload 语义漂移的问题作为 Error。<br/>
    /// Strict mode: issues that may change reload semantics are reported as errors.
    /// </summary>
    Strict,

    /// <summary>
    /// 宽松模式：可降级的问题作为 Warning。<br/>
    /// Lenient mode: downgrade recoverable issues to warnings where possible.
    /// </summary>
    Lenient,
}

/// <summary>
/// DBC writer 换行策略。<br/>
/// Newline policy used by the DBC writer.
/// </summary>
public enum DbcWriterNewLine
{
    /// <summary>
    /// 使用 LF 换行。<br/>
    /// Use line-feed newlines.
    /// </summary>
    LineFeed,

    /// <summary>
    /// 使用 CRLF 换行。<br/>
    /// Use carriage-return line-feed newlines.
    /// </summary>
    CarriageReturnLineFeed,

    /// <summary>
    /// 使用当前环境换行。<br/>
    /// Use the current environment newline.
    /// </summary>
    Environment,
}

/// <summary>
/// DBC writer 排序策略。<br/>
/// Sort mode used by the DBC writer.
/// </summary>
public enum DbcWriterSortMode
{
    /// <summary>
    /// 保持文档中的对象顺序。<br/>
    /// Preserve object order from the document.
    /// </summary>
    DocumentOrder,

    /// <summary>
    /// 使用稳定的 ordinal 名称排序。<br/>
    /// Use stable ordinal name ordering.
    /// </summary>
    Stable,
}

/// <summary>
/// DBC 名称导出策略。<br/>
/// Name export policy used by the DBC writer.
/// </summary>
public enum DbcNameExportPolicy
{
    /// <summary>
    /// 优先保留源文件结构名，并用 long symbol 保持 canonical name。<br/>
    /// Prefer source statement names and preserve canonical names with long symbols.
    /// </summary>
    PreserveSourceNamesAndLongSymbols,

    /// <summary>
    /// canonical name 合法时直接用于结构行。<br/>
    /// Use canonical names in structure lines when they are valid DBC identifiers.
    /// </summary>
    UseCanonicalNamesWhenValid,
}

/// <summary>
/// DBC writer 选项。<br/>
/// DBC writer options.
/// </summary>
public sealed class DbcWriterOptions
{
    /// <summary>
    /// 默认 writer 选项。<br/>
    /// Default writer options.
    /// </summary>
    public static DbcWriterOptions Default { get; } = new();

    /// <summary>
    /// 写出模式。<br/>
    /// Write mode.
    /// </summary>
    public DbcWriteMode Mode { get; init; } = DbcWriteMode.Strict;

    /// <summary>
    /// 换行策略。<br/>
    /// Newline policy.
    /// </summary>
    public DbcWriterNewLine NewLine { get; init; } = DbcWriterNewLine.LineFeed;

    /// <summary>
    /// 排序策略。<br/>
    /// Sort mode.
    /// </summary>
    public DbcWriterSortMode SortMode { get; init; } = DbcWriterSortMode.DocumentOrder;

    /// <summary>
    /// 名称导出策略。<br/>
    /// Name export policy.
    /// </summary>
    public DbcNameExportPolicy NameExportPolicy { get; init; } = DbcNameExportPolicy.PreserveSourceNamesAndLongSymbols;

    /// <summary>
    /// 是否输出规范化默认 header。<br/>
    /// Whether to emit the normalized default header.
    /// </summary>
    public bool IncludeDefaultHeader { get; init; } = true;

    internal string GetNewLine()
    {
        return NewLine switch
        {
            DbcWriterNewLine.LineFeed => "\n",
            DbcWriterNewLine.CarriageReturnLineFeed => "\r\n",
            DbcWriterNewLine.Environment => Environment.NewLine,
            _ => "\n",
        };
    }
}
