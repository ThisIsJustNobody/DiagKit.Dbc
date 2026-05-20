namespace DiagKit.Dbc;

/// <summary>
/// DBC 加载诊断的严重级别。<br/>
/// Severity level of a DBC loading diagnostic.
/// </summary>
public enum DbcDiagnosticSeverity
{
    /// <summary>
    /// 信息 / Informational.
    /// </summary>
    Info,

    /// <summary>
    /// 警告 / Warning.
    /// </summary>
    Warning,

    /// <summary>
    /// 错误 / Error.
    /// </summary>
    Error,
}

/// <summary>
/// DBC 加载过程中产生的结构化诊断。<br/>
/// Structured diagnostic produced during DBC loading.
/// </summary>
public sealed record DbcDiagnostic(
    DbcDiagnosticSeverity Severity,
    string Code,
    string Message,
    int LineNumber = 0);

/// <summary>
/// 同一 severity/code 的 diagnostics 分组。<br/>
/// Group of diagnostics sharing the same severity and code.
/// </summary>
public sealed record DbcDiagnosticGroup(
    DbcDiagnosticSeverity Severity,
    string Code,
    IReadOnlyList<DbcDiagnostic> Diagnostics);

/// <summary>
/// Diagnostics 的分组摘要。<br/>
/// Grouped diagnostics summary.
/// </summary>
public sealed class DbcDiagnosticSummary
{
    private readonly IReadOnlyList<DbcDiagnosticGroup> groups;

    /// <summary>
    /// 创建 diagnostics 摘要。<br/>
    /// Creates a diagnostics summary.
    /// </summary>
    public DbcDiagnosticSummary(IEnumerable<DbcDiagnosticGroup> groups)
    {
        var groupArray = (groups ?? throw new ArgumentNullException(nameof(groups))).ToArray();
        this.groups = Array.AsReadOnly(groupArray);
        ErrorCount = groupArray.Sum(x => x.Severity == DbcDiagnosticSeverity.Error ? x.Diagnostics.Count : 0);
        WarningCount = groupArray.Sum(x => x.Severity == DbcDiagnosticSeverity.Warning ? x.Diagnostics.Count : 0);
        InfoCount = groupArray.Sum(x => x.Severity == DbcDiagnosticSeverity.Info ? x.Diagnostics.Count : 0);
    }

    /// <summary>
    /// 分组列表。<br/>
    /// Diagnostic groups.
    /// </summary>
    public IReadOnlyList<DbcDiagnosticGroup> Groups => groups;

    /// <summary>
    /// Error 级 diagnostics 数量。<br/>
    /// Number of Error diagnostics.
    /// </summary>
    public int ErrorCount { get; }

    /// <summary>
    /// Warning 级 diagnostics 数量。<br/>
    /// Number of Warning diagnostics.
    /// </summary>
    public int WarningCount { get; }

    /// <summary>
    /// Info 级 diagnostics 数量。<br/>
    /// Number of Info diagnostics.
    /// </summary>
    public int InfoCount { get; }

    /// <summary>
    /// 是否存在 Error 级 diagnostics。<br/>
    /// Whether any Error diagnostics are present.
    /// </summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>
    /// 是否存在 Warning 级 diagnostics。<br/>
    /// Whether any Warning diagnostics are present.
    /// </summary>
    public bool HasWarnings => WarningCount > 0;
}

/// <summary>
/// DBC 加载选项。<br/>
/// DBC loading options.
/// </summary>
public sealed class DbcLoadOptions
{
    /// <summary>
    /// 严格模式：遇到错误时不返回可用文档。<br/>
    /// Strict mode: no document is returned when errors are present.
    /// </summary>
    public static DbcLoadOptions Strict { get; } = new(DbcLoadMode.Strict);

    /// <summary>
    /// 宽松模式：尽量保留可恢复对象，同时输出 warning/error diagnostics。<br/>
    /// Lenient mode: preserve recoverable objects while emitting warning/error diagnostics.
    /// </summary>
    public static DbcLoadOptions Lenient { get; } = new(DbcLoadMode.Lenient);

    /// <summary>
    /// 创建加载选项。<br/>
    /// Creates load options.
    /// </summary>
    public DbcLoadOptions(DbcLoadMode mode)
    {
        Mode = mode;
    }

    /// <summary>
    /// 加载模式 (Strict / Lenient)。<br/>
    /// Load mode (Strict / Lenient).
    /// </summary>
    public DbcLoadMode Mode { get; }

    /// <summary>
    /// 单条 DBC statement 的最大字符数，默认 1 MiB。<br/>
    /// Maximum character length of one DBC statement, defaulting to 1 MiB.
    /// </summary>
    public int MaxStatementLength { get; init; } = 1_048_576;
}

/// <summary>
/// DBC 加载模式。<br/>
/// DBC loading mode.
/// </summary>
public enum DbcLoadMode
{
    /// <summary>
    /// 严格模式：遇到任何 error 即丢弃文档。<br/>
    /// Strict: discard the document on any error.
    /// </summary>
    Strict,

    /// <summary>
    /// 宽松模式：尽量保留文档；可恢复结构问题降级为 warning，明确无效或越界语义仍保留 error。<br/>
    /// Lenient: preserve the document when possible; recoverable structural issues become warnings while clearly invalid or out-of-scope semantics remain errors.
    /// </summary>
    Lenient,
}

/// <summary>
/// DBC 加载结果，包含可选文档和全部 diagnostics。<br/>
/// DBC load result, containing an optional document and all diagnostics.
/// </summary>
public sealed class DbcLoadResult
{
    private readonly IReadOnlyList<DbcDiagnostic> errors;
    private readonly bool succeeded;
    private readonly IReadOnlyList<DbcDiagnostic> warnings;

    /// <summary>
    /// 创建加载结果。<br/>
    /// Creates a load result.
    /// </summary>
    public DbcLoadResult(DbcDocument? document, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        errors = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Error).ToArray());
        warnings = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Warning).ToArray());
        succeeded = Document is not null && errors.Count == 0;
    }

    /// <summary>
    /// 加载后的 DBC 文档（可能为 null）。<br/>
    /// The loaded DBC document (may be null).
    /// </summary>
    public DbcDocument? Document { get; }

    /// <summary>
    /// 加载过程中产生的所有诊断（警告和错误）。<br/>
    /// All diagnostics produced during loading (warnings and errors).
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Error 级别 diagnostics。<br/>
    /// Error-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Errors => errors;

    /// <summary>
    /// Warning 级别 diagnostics。<br/>
    /// Warning-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Warnings => warnings;

    /// <summary>
    /// 是否包含 Error 级别 diagnostics。<br/>
    /// Whether any Error-level diagnostics are present.
    /// </summary>
    public bool HasErrors => errors.Count > 0;

    /// <summary>
    /// 是否包含 Warning 级别 diagnostics。<br/>
    /// Whether any Warning-level diagnostics are present.
    /// </summary>
    public bool HasWarnings => warnings.Count > 0;

    /// <summary>
    /// 加载是否成功（有文档且无 Error 级别诊断）。<br/>
    /// Whether loading succeeded (document present and no Error-level diagnostics).
    /// </summary>
    public bool Succeeded => succeeded;

    /// <summary>
    /// 如果加载成功则返回文档；否则抛出包含首个错误诊断的异常。<br/>
    /// Returns the document on success; throws an exception with the first error diagnostic otherwise.
    /// </summary>
    public DbcDocument GetDocumentOrThrow()
    {
        if (!Succeeded)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(this));
        }

        return Document!;
    }

    /// <summary>
    /// 如果存在 Error 级别 diagnostics，则抛出格式化后的 DbcException。<br/>
    /// Throws a formatted DbcException when Error-level diagnostics are present.
    /// </summary>
    public void ThrowIfErrors()
    {
        if (HasErrors)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(this));
        }
    }
}

/// <summary>
/// DBC diagnostics 的默认文本格式化器，适合日志、异常和首次接入 UI 展示。<br/>
/// Default text formatter for DBC diagnostics, suitable for logs, exceptions, and first-use UI display.
/// </summary>
public static class DbcDiagnosticFormatter
{
    /// <summary>
    /// 汇总 diagnostics，按 severity/code 分组。<br/>
    /// Summarizes diagnostics grouped by severity and code.
    /// </summary>
    public static DbcDiagnosticSummary Summarize(IEnumerable<DbcDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var groups = diagnostics
            .GroupBy(x => new { x.Severity, x.Code })
            .OrderBy(x => GetSeverityRank(x.Key.Severity))
            .ThenBy(x => x.Key.Code, StringComparer.Ordinal)
            .Select(x => new DbcDiagnosticGroup(
                x.Key.Severity,
                x.Key.Code,
                Array.AsReadOnly(x.OrderBy(d => d.LineNumber).ToArray())))
            .ToArray();
        return new DbcDiagnosticSummary(groups);
    }

    /// <summary>
    /// 格式化加载结果中的 diagnostics。<br/>
    /// Formats diagnostics from a load result.
    /// </summary>
    public static string Format(DbcLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Format(result.Diagnostics);
    }

    /// <summary>
    /// 格式化 diagnostics 集合。<br/>
    /// Formats a diagnostics collection.
    /// </summary>
    public static string Format(IEnumerable<DbcDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var items = diagnostics.ToArray();
        if (items.Length == 0)
        {
            return "DBC diagnostics: none.";
        }

        var errorCount = items.Count(x => x.Severity == DbcDiagnosticSeverity.Error);
        var warningCount = items.Count(x => x.Severity == DbcDiagnosticSeverity.Warning);
        var infoCount = items.Count(x => x.Severity == DbcDiagnosticSeverity.Info);
        var builder = new System.Text.StringBuilder();
        builder.Append("DBC diagnostics: ");
        builder.Append(errorCount);
        builder.Append(" error(s), ");
        builder.Append(warningCount);
        builder.Append(" warning(s), ");
        builder.Append(infoCount);
        builder.Append(" info.");

        foreach (var diagnostic in items
            .OrderBy(x => GetSeverityRank(x.Severity))
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.LineNumber))
        {
            builder.AppendLine();
            builder.Append(diagnostic.Severity);
            builder.Append(' ');
            builder.Append(diagnostic.Code);
            if (diagnostic.LineNumber > 0)
            {
                builder.Append(" line ");
                builder.Append(diagnostic.LineNumber);
            }

            builder.Append(": ");
            builder.Append(diagnostic.Message);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按 severity/code 分组格式化 diagnostics。<br/>
    /// Formats diagnostics grouped by severity and code.
    /// </summary>
    public static string FormatGrouped(IEnumerable<DbcDiagnostic> diagnostics)
    {
        var summary = Summarize(diagnostics);
        if (summary.Groups.Count == 0)
        {
            return "DBC diagnostics: none.";
        }

        var builder = new System.Text.StringBuilder();
        builder.Append("DBC diagnostics: ");
        builder.Append(summary.ErrorCount);
        builder.Append(" error(s), ");
        builder.Append(summary.WarningCount);
        builder.Append(" warning(s), ");
        builder.Append(summary.InfoCount);
        builder.Append(" info.");

        foreach (var group in summary.Groups)
        {
            builder.AppendLine();
            builder.Append(group.Severity);
            builder.Append(' ');
            builder.Append(group.Code);
            builder.Append(" (");
            builder.Append(group.Diagnostics.Count);
            builder.Append(')');

            foreach (var diagnostic in group.Diagnostics)
            {
                builder.AppendLine();
                builder.Append("  ");
                if (diagnostic.LineNumber > 0)
                {
                    builder.Append("line ");
                    builder.Append(diagnostic.LineNumber);
                    builder.Append(": ");
                }

                builder.Append(diagnostic.Message);
            }
        }

        return builder.ToString();
    }

    private static int GetSeverityRank(DbcDiagnosticSeverity severity)
    {
        return severity switch
        {
            DbcDiagnosticSeverity.Error => 0,
            DbcDiagnosticSeverity.Warning => 1,
            _ => 2,
        };
    }
}
