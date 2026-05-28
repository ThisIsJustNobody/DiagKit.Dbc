using System.Text;

namespace DiagKit.Dbc;

/// <summary>
/// 规范化 DBC writer。<br/>
/// Normalized DBC writer.
/// </summary>
public static class DbcWriter
{
    /// <summary>
    /// 将 DBC 文档写出为规范化文本。<br/>
    /// Writes a DBC document as normalized text.
    /// </summary>
    public static DbcWriteResult WriteText(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var validation = DbcWriteValidator.Validate(document, options);
        if (validation.HasErrors)
        {
            return new DbcWriteResult(null, validation.Diagnostics);
        }

        return new DbcWriteResult(WriteCore(document, options), validation.Diagnostics);
    }

    /// <summary>
    /// 将 DBC 文档写出为规范化文本；存在 Error 时抛出 DbcException。<br/>
    /// Writes a DBC document as normalized text; throws DbcException when errors are present.
    /// </summary>
    public static string WriteTextOrThrow(DbcDocument document, DbcWriterOptions? options = null)
    {
        return WriteText(document, options).GetTextOrThrow();
    }

    /// <summary>
    /// 将 DBC 文档写出到文件。<br/>
    /// Writes a DBC document to a file.
    /// </summary>
    public static DbcWriteResult WriteFile(string path, DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = WriteText(document, options);
        if (result.Succeeded)
        {
            File.WriteAllText(path, result.Text!);
        }

        return result;
    }

    /// <summary>
    /// 将 DBC 文档写出到文件；存在 Error 时抛出 DbcException。<br/>
    /// Writes a DBC document to a file; throws DbcException when errors are present.
    /// </summary>
    public static void WriteFileOrThrow(string path, DbcDocument document, DbcWriterOptions? options = null)
    {
        WriteFile(path, document, options).ThrowIfErrors();
    }

    /// <summary>
    /// 将 DBC 文档写出到 TextWriter。<br/>
    /// Writes a DBC document to a TextWriter.
    /// </summary>
    public static DbcWriteResult Write(TextWriter writer, DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var result = WriteText(document, options);
        if (result.Succeeded)
        {
            writer.Write(result.Text);
        }

        return result;
    }

    private static string WriteCore(DbcDocument document, DbcWriterOptions options)
    {
        var newline = options.GetNewLine();
        var builder = new StringBuilder();
        if (options.IncludeDefaultHeader)
        {
            builder.Append("VERSION \"\"").Append(newline).Append(newline);
            builder.Append("NS_ :").Append(newline);
            builder.Append("    NS_DESC_").Append(newline);
            builder.Append("    CM_").Append(newline);
            builder.Append("    BA_DEF_").Append(newline);
            builder.Append("    BA_").Append(newline);
            builder.Append("    VAL_").Append(newline);
            builder.Append("    BA_DEF_DEF_").Append(newline);
            builder.Append("    EV_").Append(newline);
            builder.Append("    SIG_VALTYPE_").Append(newline);
            builder.Append("    BO_TX_BU_").Append(newline);
            builder.Append("    BA_DEF_REL_").Append(newline);
            builder.Append("    BA_REL_").Append(newline);
            builder.Append("    BA_DEF_DEF_REL_").Append(newline);
            builder.Append("    SG_MUL_VAL_").Append(newline).Append(newline);
            builder.Append("BS_:").Append(newline).Append(newline);
        }

        builder.Append("BU_:");
        foreach (var node in GetNodes(document, options))
        {
            builder.Append(' ').Append(DbcWriterNameFormatter.GetNodeExportName(node, options));
        }

        builder.Append(newline);
        return builder.ToString();
    }

    private static IEnumerable<DbcNode> GetNodes(DbcDocument document, DbcWriterOptions options)
    {
        return options.SortMode == DbcWriterSortMode.Stable
            ? document.Nodes.OrderBy(node => DbcWriterNameFormatter.GetNodeExportName(node, options), StringComparer.Ordinal)
            : document.Nodes;
    }
}

internal static class DbcWriterNameFormatter
{
    internal static string GetNodeExportName(DbcNode node, DbcWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(options);

        return options.NameExportPolicy == DbcNameExportPolicy.UseCanonicalNamesWhenValid &&
            DbcWriteValidator.IsValidIdentifier(node.Name)
                ? node.Name
                : node.SourceName;
    }
}
