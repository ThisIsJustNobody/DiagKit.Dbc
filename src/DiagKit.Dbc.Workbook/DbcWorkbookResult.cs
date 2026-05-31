namespace DiagKit.Dbc.Workbook;

/// <summary>
/// DBC Excel 导出结果。<br/>
/// DBC Excel export result.
/// </summary>
public sealed class DbcWorkbookExportResult
{
    private readonly IReadOnlyList<DbcDiagnostic> errors;
    private readonly IReadOnlyList<DbcDiagnostic> warnings;

    /// <summary>
    /// 创建导出结果。<br/>
    /// Creates an export result.
    /// </summary>
    public DbcWorkbookExportResult(byte[]? workbookBytes, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        WorkbookBytes = workbookBytes;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        errors = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Error).ToArray());
        warnings = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Warning).ToArray());
    }

    /// <summary>
    /// `.xlsx` DBC Excel bytes。<br/>
    /// `.xlsx` DBC Excel bytes.
    /// </summary>
    public byte[]? WorkbookBytes { get; }

    /// <summary>
    /// Diagnostics。<br/>
    /// Diagnostics.
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
    /// 是否成功。<br/>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Succeeded => WorkbookBytes is not null && errors.Count == 0;

    /// <summary>
    /// 成功时返回 DBC Excel bytes，否则抛出 DbcException。<br/>
    /// Returns DBC Excel bytes on success; throws DbcException otherwise.
    /// </summary>
    public byte[] GetWorkbookBytesOrThrow()
    {
        if (!Succeeded)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(Diagnostics));
        }

        return WorkbookBytes!;
    }

    /// <summary>
    /// 如果存在 Error 级 diagnostics，则抛出 DbcException。<br/>
    /// Throws DbcException when Error-level diagnostics are present.
    /// </summary>
    public void ThrowIfErrors()
    {
        if (errors.Count > 0)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(Diagnostics));
        }
    }
}

/// <summary>
/// DBC Excel 导入结果。<br/>
/// DBC Excel import result.
/// </summary>
public sealed class DbcWorkbookImportResult
{
    private readonly IReadOnlyList<DbcDiagnostic> errors;
    private readonly IReadOnlyList<DbcDiagnostic> warnings;

    /// <summary>
    /// 创建导入结果。<br/>
    /// Creates an import result.
    /// </summary>
    public DbcWorkbookImportResult(DbcDocument? document, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        errors = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Error).ToArray());
        warnings = Array.AsReadOnly(Diagnostics.Where(x => x.Severity == DbcDiagnosticSeverity.Warning).ToArray());
    }

    /// <summary>
    /// 导入后的 DBC 文档。<br/>
    /// Imported DBC document.
    /// </summary>
    public DbcDocument? Document { get; }

    /// <summary>
    /// Diagnostics。<br/>
    /// Diagnostics.
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
    /// 是否成功。<br/>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Succeeded => Document is not null && errors.Count == 0;

    /// <summary>
    /// 成功时返回 DBC 文档，否则抛出 DbcException。<br/>
    /// Returns the DBC document on success; throws DbcException otherwise.
    /// </summary>
    public DbcDocument GetDocumentOrThrow()
    {
        if (!Succeeded)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(Diagnostics));
        }

        return Document!;
    }

    /// <summary>
    /// 如果存在 Error 级 diagnostics，则抛出 DbcException。<br/>
    /// Throws DbcException when Error-level diagnostics are present.
    /// </summary>
    public void ThrowIfErrors()
    {
        if (errors.Count > 0)
        {
            throw new DbcException(DbcDiagnosticFormatter.Format(Diagnostics));
        }
    }
}
