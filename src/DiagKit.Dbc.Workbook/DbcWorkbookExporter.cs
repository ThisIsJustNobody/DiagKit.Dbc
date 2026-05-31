using System.Globalization;

namespace DiagKit.Dbc.Workbook;

/// <summary>
/// DBC Excel 格式导出器。<br/>
/// DBC Excel format exporter.
/// </summary>
public static class DbcWorkbookExporter
{
    private const string EmptyNodeName = "Vector__XXX";
    private const string VectorIndependentMessageName = "VECTOR__INDEPENDENT_SIG_MSG";

    /// <summary>
    /// 将 DBC 文档导出为 `.xlsx` bytes。<br/>
    /// Exports a DBC document to `.xlsx` bytes.
    /// </summary>
    public static DbcWorkbookExportResult ExportDocument(DbcDocument document, DbcWorkbookExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWorkbookExportOptions.Default;

        return new DbcWorkbookExportResult(CreateWorkbook(document, options).Save(), []);
    }

    /// <summary>
    /// 导出空白 DBC Excel 模板。<br/>
    /// Exports a blank DBC Excel template.
    /// </summary>
    public static DbcWorkbookExportResult ExportTemplate(DbcWorkbookExportOptions? options = null)
    {
        options ??= DbcWorkbookExportOptions.Default;
        return new DbcWorkbookExportResult(CreateWorkbook(null, options).Save(), []);
    }

    /// <summary>
    /// 从 DBC 文件导出 `.xlsx`。<br/>
    /// Exports a DBC file to `.xlsx`.
    /// </summary>
    public static DbcWorkbookExportResult ExportFile(string dbcPath, DbcLoadOptions? loadOptions = null, DbcWorkbookExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbcPath);
        var loadResult = DbcLoader.LoadFile(dbcPath, loadOptions ?? DbcLoadOptions.Lenient);
        if (!loadResult.Succeeded)
        {
            return new DbcWorkbookExportResult(null, loadResult.Diagnostics);
        }

        var exportResult = ExportDocument(loadResult.Document!, options);
        return new DbcWorkbookExportResult(exportResult.WorkbookBytes, loadResult.Diagnostics.Concat(exportResult.Diagnostics).ToArray());
    }

    /// <summary>
    /// 将 DBC 文档导出并写入 `.xlsx` 文件。<br/>
    /// Exports a DBC document and writes the `.xlsx` file.
    /// </summary>
    public static DbcWorkbookExportResult WriteWorkbook(string path, DbcDocument document, DbcWorkbookExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = ExportDocument(document, options);
        if (result.Succeeded)
        {
            File.WriteAllBytes(path, result.WorkbookBytes!);
        }

        return result;
    }

    /// <summary>
    /// 将 DBC 文档导出并写入 `.xlsx` 文件；失败时抛出 DbcException。<br/>
    /// Exports a DBC document and writes the `.xlsx` file; throws DbcException on failure.
    /// </summary>
    public static void WriteWorkbookOrThrow(string path, DbcDocument document, DbcWorkbookExportOptions? options = null)
    {
        WriteWorkbook(path, document, options).ThrowIfErrors();
    }

    /// <summary>
    /// 写入空白 DBC Excel 模板。<br/>
    /// Writes a blank DBC Excel template.
    /// </summary>
    public static DbcWorkbookExportResult WriteTemplate(string path, DbcWorkbookExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = ExportTemplate(options);
        if (result.Succeeded)
        {
            File.WriteAllBytes(path, result.WorkbookBytes!);
        }

        return result;
    }

    /// <summary>
    /// 写入空白 DBC Excel 模板；失败时抛出 DbcException。<br/>
    /// Writes a blank DBC Excel template; throws DbcException on failure.
    /// </summary>
    public static void WriteTemplateOrThrow(string path, DbcWorkbookExportOptions? options = null)
    {
        WriteTemplate(path, options).ThrowIfErrors();
    }

    private static SpreadsheetWorkbook CreateWorkbook(DbcDocument? document, DbcWorkbookExportOptions options)
    {
        _ = options;
        var workbook = new SpreadsheetWorkbook();
        workbook.AddSheet(DbcWorkbookSchema.NetworkSheet, DbcWorkbookSchema.NetworkHeaders, document is null ? [] : CreateNetworkRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.NodesSheet, DbcWorkbookSchema.NodeHeaders, document is null ? [] : CreateNodeRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.MessagesSheet, DbcWorkbookSchema.MessageHeaders, document is null ? [] : CreateMessageRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.SignalsSheet, DbcWorkbookSchema.SignalHeaders, document is null ? [] : CreateSignalRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.ValueDescriptionsSheet, DbcWorkbookSchema.ValueDescriptionHeaders, document is null ? [] : CreateValueDescriptionRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.MultiplexRangesSheet, DbcWorkbookSchema.MultiplexRangeHeaders, document is null ? [] : CreateMultiplexRangeRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.EnvironmentVariablesSheet, DbcWorkbookSchema.EnvironmentVariableHeaders, document is null ? [] : CreateEnvironmentVariableRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.AttributeDefinitionsSheet, DbcWorkbookSchema.AttributeDefinitionHeaders, document is null ? [] : CreateAttributeDefinitionRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.AttributesSheet, DbcWorkbookSchema.AttributeHeaders, document is null ? [] : CreateAttributeRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.RelationAttributeDefinitionsSheet, DbcWorkbookSchema.RelationAttributeDefinitionHeaders, document is null ? [] : CreateRelationAttributeDefinitionRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.RelationAttributeDefaultsSheet, DbcWorkbookSchema.RelationAttributeDefaultHeaders, document is null ? [] : CreateRelationAttributeDefaultRows(document), hiddenColumns: 0);
        workbook.AddSheet(DbcWorkbookSchema.RelationAttributesSheet, DbcWorkbookSchema.RelationAttributeHeaders, document is null ? [] : CreateRelationAttributeRows(document), hiddenColumns: 0);
        return workbook;
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateNetworkRows(DbcDocument document)
    {
        if (!string.IsNullOrEmpty(document.Comment))
        {
            yield return [document.Comment];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateNodeRows(DbcDocument document)
    {
        foreach (var node in document.Nodes)
        {
            if (!IsEmptyNodeName(node.Name))
            {
                yield return [node.Name, node.Comment ?? string.Empty];
            }
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateMessageRows(DbcDocument document)
    {
        foreach (var message in document.Messages)
        {
            if (IsVectorIndependentMessage(message))
            {
                continue;
            }

            yield return
            [
                message.Name,
                message.Identifier.Value,
                message.Identifier.Format.ToString(),
                message.DataLength,
                message.IsCanFd ? "TRUE" : "FALSE",
                JoinNodeNames(message.Transmitters),
                message.CycleTimeMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                message.SendType == DbcSendType.Unknown ? string.Empty : message.SendType.ToString(),
                message.TimeoutTimeMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                message.Comment ?? string.Empty,
            ];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateSignalRows(DbcDocument document)
    {
        foreach (var message in document.Messages)
        {
            if (IsVectorIndependentMessage(message))
            {
                continue;
            }

            foreach (var signal in message.Signals)
            {
                yield return
                [
                    message.Name,
                    signal.Name,
                    signal.StartBit,
                    signal.BitLength,
                    signal.ByteOrder.ToString(),
                    signal.ValueType.ToString(),
                    signal.Factor,
                    signal.Offset,
                    signal.Minimum,
                    signal.Maximum,
                    signal.Unit,
                    JoinNodeNames(signal.Receivers),
                    GetMultiplexingRoleText(signal.Multiplexing),
                    signal.Multiplexing.SwitchValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    signal.Multiplexing.MultiplexorSignalName ?? string.Empty,
                    signal.InitialValue?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                    signal.SendType == DbcSendType.Unknown ? string.Empty : signal.SendType.ToString(),
                    signal.TimeoutTimeMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    signal.Comment ?? string.Empty,
                ];
            }
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateValueDescriptionRows(DbcDocument document)
    {
        foreach (var message in document.Messages)
        {
            if (IsVectorIndependentMessage(message))
            {
                continue;
            }

            foreach (var signal in message.Signals)
            {
                foreach (var valueDescription in signal.ValueDescriptions.OrderBy(item => item.Key))
                {
                    yield return [message.Name, signal.Name, valueDescription.Key, valueDescription.Value];
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateMultiplexRangeRows(DbcDocument document)
    {
        foreach (var message in document.Messages)
        {
            if (IsVectorIndependentMessage(message))
            {
                continue;
            }

            foreach (var signal in message.Signals)
            {
                foreach (var range in signal.Multiplexing.SwitchRanges)
                {
                    yield return
                    [
                        message.Name,
                        signal.Name,
                        signal.Multiplexing.MultiplexorSignalName ?? string.Empty,
                        range.Minimum,
                        range.Maximum,
                    ];
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateEnvironmentVariableRows(DbcDocument document)
    {
        foreach (var variable in document.EnvironmentVariables.Values.OrderBy(variable => variable.Name, StringComparer.Ordinal))
        {
            yield return
            [
                variable.Name,
                variable.ValueType,
                variable.Minimum,
                variable.Maximum,
                variable.Unit,
                variable.InitialValue,
                variable.Identifier,
                variable.AccessType,
                JoinNodeNames(variable.AccessNodes),
            ];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateAttributeDefinitionRows(DbcDocument document)
    {
        foreach (var definition in document.AttributeDefinitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            if (IsWorkbookManagedAttribute(definition.Name))
            {
                continue;
            }

            yield return
            [
                definition.OwnerKind.ToString(),
                definition.Name,
                definition.ValueKind.ToString(),
                definition.Minimum?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                definition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                string.Join(";", definition.EnumValues),
                definition.DefaultValue?.RawValue ?? string.Empty,
            ];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateAttributeRows(DbcDocument document)
    {
        foreach (var attribute in document.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            if (!IsWorkbookManagedAttribute(attribute.Name))
            {
                yield return ["Network", string.Empty, string.Empty, string.Empty, string.Empty, attribute.Name, attribute.RawValue];
            }
        }

        foreach (var message in document.Messages)
        {
            if (IsVectorIndependentMessage(message))
            {
                continue;
            }

            foreach (var attribute in message.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                if (!IsWorkbookManagedAttribute(attribute.Name))
                {
                    yield return ["Message", message.Name, string.Empty, string.Empty, string.Empty, attribute.Name, attribute.RawValue];
                }
            }

            foreach (var signal in message.Signals)
            {
                foreach (var attribute in signal.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    if (!IsWorkbookManagedAttribute(attribute.Name))
                    {
                        yield return ["Signal", message.Name, signal.Name, string.Empty, string.Empty, attribute.Name, attribute.RawValue];
                    }
                }
            }
        }

        foreach (var node in document.Nodes)
        {
            foreach (var attribute in node.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                if (!IsEmptyNodeName(node.Name) && !IsWorkbookManagedAttribute(attribute.Name))
                {
                    yield return ["Node", string.Empty, string.Empty, node.Name, string.Empty, attribute.Name, attribute.RawValue];
                }
            }
        }

        foreach (var variable in document.EnvironmentVariables.Values.OrderBy(variable => variable.Name, StringComparer.Ordinal))
        {
            foreach (var attribute in variable.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                if (!IsWorkbookManagedAttribute(attribute.Name))
                {
                    yield return ["EnvironmentVariable", string.Empty, string.Empty, string.Empty, variable.Name, attribute.Name, attribute.RawValue];
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateRelationAttributeDefinitionRows(DbcDocument document)
    {
        foreach (var definition in document.RelationAttributeDefinitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            yield return
            [
                definition.RelationKind,
                definition.Name,
                definition.ValueKind.ToString(),
                definition.Minimum?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                definition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                string.Join(";", definition.EnumValues),
            ];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateRelationAttributeDefaultRows(DbcDocument document)
    {
        foreach (var value in document.RelationAttributeDefaults.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            yield return [value.Name, value.RawValue];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> CreateRelationAttributeRows(DbcDocument document)
    {
        foreach (var value in document.RelationAttributes.OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Target, StringComparer.Ordinal))
        {
            yield return [value.Name, value.Target, value.RawValue];
        }
    }

    private static string JoinNodeNames(IReadOnlyList<DbcNode> nodes)
    {
        return string.Join(";", nodes.Select(node => node.Name).Where(name => !IsEmptyNodeName(name)));
    }

    private static string GetMultiplexingRoleText(DbcMultiplexing multiplexing)
    {
        return multiplexing.Role == DbcMultiplexingRole.None ? string.Empty : multiplexing.Role.ToString();
    }

    private static bool IsEmptyNodeName(string name)
    {
        return string.Equals(name, EmptyNodeName, StringComparison.Ordinal);
    }

    private static bool IsWorkbookManagedAttribute(string attributeName)
    {
        return string.Equals(attributeName, "GenMsgCycleTime", StringComparison.Ordinal) ||
            string.Equals(attributeName, "GenMsgSendType", StringComparison.Ordinal) ||
            string.Equals(attributeName, "GenMsgTimeoutTime", StringComparison.Ordinal) ||
            string.Equals(attributeName, "GenSigSendType", StringComparison.Ordinal) ||
            string.Equals(attributeName, "GenSigStartValue", StringComparison.Ordinal) ||
            string.Equals(attributeName, "GenSigTimeoutTime", StringComparison.Ordinal) ||
            string.Equals(attributeName, "VFrameFormat", StringComparison.Ordinal) ||
            string.Equals(attributeName, "SystemNodeLongSymbol", StringComparison.Ordinal) ||
            string.Equals(attributeName, "SystemMessageLongSymbol", StringComparison.Ordinal) ||
            string.Equals(attributeName, "SystemSignalLongSymbol", StringComparison.Ordinal) ||
            string.Equals(attributeName, "SystemEnvVarLongSymbol", StringComparison.Ordinal);
    }

    private static bool IsVectorIndependentMessage(DbcMessage message)
    {
        return string.Equals(message.Name, VectorIndependentMessageName, StringComparison.Ordinal);
    }
}
