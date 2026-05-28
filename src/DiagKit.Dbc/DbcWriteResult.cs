namespace DiagKit.Dbc;

/// <summary>
/// DBC 写出前 validation 结果。<br/>
/// Validation result used before writing DBC text.
/// </summary>
public sealed class DbcValidationResult
{
    private readonly IReadOnlyList<DbcDiagnostic> errors;
    private readonly IReadOnlyList<DbcDiagnostic> warnings;

    /// <summary>
    /// 创建 validation 结果。<br/>
    /// Creates a validation result.
    /// </summary>
    public DbcValidationResult(IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        errors = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Error).ToArray());
        warnings = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Warning).ToArray());
    }

    /// <summary>
    /// 全部 diagnostics。<br/>
    /// All diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Error 级 diagnostics。<br/>
    /// Error-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Errors => errors;

    /// <summary>
    /// Warning 级 diagnostics。<br/>
    /// Warning-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Warnings => warnings;

    /// <summary>
    /// 是否包含 Error 级 diagnostics。<br/>
    /// Whether any Error-level diagnostics are present.
    /// </summary>
    public bool HasErrors => errors.Count > 0;

    /// <summary>
    /// 是否包含 Warning 级 diagnostics。<br/>
    /// Whether any Warning-level diagnostics are present.
    /// </summary>
    public bool HasWarnings => warnings.Count > 0;

    /// <summary>
    /// Validation 是否成功。<br/>
    /// Whether validation succeeded.
    /// </summary>
    public bool Succeeded => !HasErrors;

    /// <summary>
    /// 如果存在 Error 级 diagnostics，则抛出格式化后的 DbcException。<br/>
    /// Throws a formatted DbcException when Error-level diagnostics are present.
    /// </summary>
    public void ThrowIfErrors()
    {
        if (HasErrors)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(Diagnostics));
        }
    }
}

/// <summary>
/// DBC writer 结果，包含规范化文本和 diagnostics。<br/>
/// DBC writer result with normalized text and diagnostics.
/// </summary>
public sealed class DbcWriteResult
{
    private readonly DbcValidationResult validation;

    /// <summary>
    /// 创建 writer 结果。<br/>
    /// Creates a writer result.
    /// </summary>
    public DbcWriteResult(string? text, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        Text = text;
        validation = new DbcValidationResult(diagnostics);
    }

    /// <summary>
    /// 生成的 DBC 文本；存在 Error 时为 null。<br/>
    /// Generated DBC text; null when errors are present.
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// 全部 diagnostics。<br/>
    /// All diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Diagnostics => validation.Diagnostics;

    /// <summary>
    /// Error 级 diagnostics。<br/>
    /// Error-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Errors => validation.Errors;

    /// <summary>
    /// Warning 级 diagnostics。<br/>
    /// Warning-level diagnostics.
    /// </summary>
    public IReadOnlyList<DbcDiagnostic> Warnings => validation.Warnings;

    /// <summary>
    /// 是否包含 Error 级 diagnostics。<br/>
    /// Whether any Error-level diagnostics are present.
    /// </summary>
    public bool HasErrors => validation.HasErrors;

    /// <summary>
    /// 是否包含 Warning 级 diagnostics。<br/>
    /// Whether any Warning-level diagnostics are present.
    /// </summary>
    public bool HasWarnings => validation.HasWarnings;

    /// <summary>
    /// 写出是否成功。<br/>
    /// Whether writing succeeded.
    /// </summary>
    public bool Succeeded => Text is not null && !HasErrors;

    /// <summary>
    /// 成功时返回文本；否则抛出 DbcException。<br/>
    /// Returns the text on success; otherwise throws a DbcException.
    /// </summary>
    public string GetTextOrThrow()
    {
        ThrowIfErrors();
        return Text ?? throw new DbcException("DBC write produced no text.");
    }

    /// <summary>
    /// 如果存在 Error 级 diagnostics，则抛出格式化后的 DbcException。<br/>
    /// Throws a formatted DbcException when Error-level diagnostics are present.
    /// </summary>
    public void ThrowIfErrors()
    {
        validation.ThrowIfErrors();
    }
}
