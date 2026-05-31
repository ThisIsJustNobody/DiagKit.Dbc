namespace DiagKit.Dbc.Workbook;

/// <summary>
/// DBC Excel 导出选项。<br/>
/// DBC Excel export options.
/// </summary>
public sealed class DbcWorkbookExportOptions
{
    /// <summary>
    /// 默认导出选项。<br/>
    /// Default export options.
    /// </summary>
    public static DbcWorkbookExportOptions Default { get; } = new();
}

/// <summary>
/// DBC Excel 导入选项。<br/>
/// DBC Excel import options.
/// </summary>
public sealed class DbcWorkbookImportOptions
{
    /// <summary>
    /// 默认导入选项。<br/>
    /// Default import options.
    /// </summary>
    public static DbcWorkbookImportOptions Default { get; } = new();

    /// <summary>
    /// 导入后用于 normalized DBC writer validation 的选项。<br/>
    /// Writer options used for normalized DBC validation after import.
    /// </summary>
    public DbcWriterOptions? WriterOptions { get; init; }
}
