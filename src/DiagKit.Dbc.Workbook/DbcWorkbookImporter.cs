using System.Globalization;

namespace DiagKit.Dbc.Workbook;

/// <summary>
/// DBC Excel 格式导入器。<br/>
/// DBC Excel format importer.
/// </summary>
public static class DbcWorkbookImporter
{
    private const string EmptyNodeName = "Vector__XXX";
    private const string VectorIndependentMessageName = "VECTOR__INDEPENDENT_SIG_MSG";

    /// <summary>
    /// 从 DBC Excel bytes 导入 DBC 文档。<br/>
    /// Imports a DBC document from DBC Excel bytes.
    /// </summary>
    public static DbcWorkbookImportResult ImportWorkbook(byte[] workbookBytes, DbcWorkbookImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        options ??= DbcWorkbookImportOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        SpreadsheetWorkbook workbook;
        try
        {
            workbook = SpreadsheetWorkbook.Load(workbookBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or DbcException or System.Xml.XmlException)
        {
            diagnostics.Add(Error("DBC_WORKBOOK_INVALID_FILE", $"Workbook is not a readable .xlsx file: {exception.Message}"));
            return new DbcWorkbookImportResult(null, diagnostics);
        }

        if (!TryGetSheets(workbook, diagnostics, out var sheets))
        {
            return new DbcWorkbookImportResult(null, diagnostics);
        }

        var networkComment = BuildNetworkComment(sheets.Network, diagnostics);
        var messageRows = BuildMessageRowIndex(sheets.Messages, diagnostics);
        var signalRows = BuildSignalRowIndex(sheets.Signals, messageRows.Keys, diagnostics);
        ValidateValueDescriptionRows(Rows(sheets.ValueDescriptions), signalRows.KnownSignals, diagnostics);
        var multiplexRanges = BuildMultiplexRangeIndex(Rows(sheets.MultiplexRanges), signalRows.KnownSignals, diagnostics);
        var environmentRows = BuildEnvironmentVariableRowIndex(Rows(sheets.EnvironmentVariables), diagnostics);

        if (diagnostics.Any(IsError))
        {
            return new DbcWorkbookImportResult(null, diagnostics);
        }

        var attributeDefinitions = BuildAttributeDefinitions(Rows(sheets.AttributeDefinitions), diagnostics);
        var relationAttributeDefinitions = BuildRelationAttributeDefinitions(Rows(sheets.RelationAttributeDefinitions), diagnostics);
        var relationAttributeDefaults = BuildRelationAttributeDefaults(Rows(sheets.RelationAttributeDefaults), relationAttributeDefinitions, diagnostics);
        var relationAttributes = BuildRelationAttributes(Rows(sheets.RelationAttributes), relationAttributeDefinitions, diagnostics);
        var networkAttributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
        var nodeAttributeRows = new Dictionary<string, Dictionary<string, DbcAttributeValue>>(StringComparer.Ordinal);
        var messageAttributeRows = new Dictionary<string, Dictionary<string, DbcAttributeValue>>(StringComparer.Ordinal);
        var signalAttributeRows = new Dictionary<SignalReference, Dictionary<string, DbcAttributeValue>>();
        var environmentVariableAttributeRows = new Dictionary<string, Dictionary<string, DbcAttributeValue>>(StringComparer.Ordinal);
        ApplyAttributeRows(
            Rows(sheets.Attributes),
            messageRows.Keys,
            signalRows.KnownSignals,
            environmentRows.Keys,
            attributeDefinitions,
            networkAttributes,
            nodeAttributeRows,
            messageAttributeRows,
            signalAttributeRows,
            environmentVariableAttributeRows,
            diagnostics);

        var nodesByName = BuildNodes(Rows(sheets.Nodes), nodeAttributeRows, diagnostics);
        var environmentVariables = BuildEnvironmentVariables(environmentRows, environmentVariableAttributeRows, nodesByName, diagnostics);
        var messages = new List<DbcMessage>();
        foreach (var row in messageRows.Values)
        {
            var message = BuildMessage(
                row,
                signalRows.RowsByMessage.TryGetValue(row.Get("message_name"), out var rows) ? rows : [],
                Rows(sheets.ValueDescriptions),
                multiplexRanges,
                messageAttributeRows,
                signalAttributeRows,
                attributeDefinitions,
                nodesByName,
                diagnostics);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        if (diagnostics.Any(IsError))
        {
            return new DbcWorkbookImportResult(null, diagnostics);
        }

        var document = new DbcDocument(
            nodesByName.Values.Distinct().Where(node => !IsEmptyNodeName(node.Name)).OrderBy(node => node.Name, StringComparer.Ordinal).ToArray(),
            messages,
            attributeDefinitions,
            networkAttributes,
            networkComment,
            environmentVariables,
            relationAttributeDefinitions,
            relationAttributeDefaults,
            relationAttributes);

        var writeResult = DbcWriter.WriteText(document, options.WriterOptions);
        diagnostics.AddRange(writeResult.Diagnostics);
        return writeResult.Succeeded
            ? new DbcWorkbookImportResult(document, diagnostics)
            : new DbcWorkbookImportResult(null, diagnostics);
    }

    /// <summary>
    /// 从 DBC Excel 文件导入 DBC 文档。<br/>
    /// Imports a DBC document from a DBC Excel file.
    /// </summary>
    public static DbcWorkbookImportResult ImportWorkbookFile(string workbookPath, DbcWorkbookImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        return ImportWorkbook(File.ReadAllBytes(workbookPath), options);
    }

    private static bool TryGetSheets(SpreadsheetWorkbook workbook, List<DbcDiagnostic> diagnostics, out WorkbookSheets sheets)
    {
        sheets = null!;
        if (HasSheet(workbook, "_Readme") || HasSheet(workbook, "_Manifest"))
        {
            diagnostics.Add(Error(
                "DBC_WORKBOOK_LIBRARY_METADATA_SHEET",
                "Workbook contains legacy library metadata sheets (_Readme/_Manifest). Re-export as the DBC Excel format and import the Excel file directly."));
            return false;
        }

        var messages = GetSheetOrNull(workbook, DbcWorkbookSchema.MessagesSheet);
        var signals = GetSheetOrNull(workbook, DbcWorkbookSchema.SignalsSheet);
        if (messages is null)
        {
            diagnostics.Add(Error("DBC_WORKBOOK_MISSING_SHEET", "Workbook sheet 'Messages' was not found."));
        }

        if (signals is null)
        {
            diagnostics.Add(Error("DBC_WORKBOOK_MISSING_SHEET", "Workbook sheet 'Signals' was not found."));
        }

        if (messages is null || signals is null)
        {
            return false;
        }

        ValidateHeaders(messages, DbcWorkbookSchema.MessageHeaders, diagnostics);
        ValidateHeaders(signals, DbcWorkbookSchema.SignalHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.NetworkSheet), DbcWorkbookSchema.NetworkHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.NodesSheet), DbcWorkbookSchema.NodeHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.ValueDescriptionsSheet), DbcWorkbookSchema.ValueDescriptionHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.MultiplexRangesSheet), DbcWorkbookSchema.MultiplexRangeHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.EnvironmentVariablesSheet), DbcWorkbookSchema.EnvironmentVariableHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.AttributeDefinitionsSheet), DbcWorkbookSchema.AttributeDefinitionHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.AttributesSheet), DbcWorkbookSchema.AttributeHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributeDefinitionsSheet), DbcWorkbookSchema.RelationAttributeDefinitionHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributeDefaultsSheet), DbcWorkbookSchema.RelationAttributeDefaultHeaders, diagnostics);
        ValidateOptionalHeaders(GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributesSheet), DbcWorkbookSchema.RelationAttributeHeaders, diagnostics);

        if (diagnostics.Any(IsError))
        {
            return false;
        }

        sheets = new WorkbookSheets(
            GetSheetOrNull(workbook, DbcWorkbookSchema.NetworkSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.NodesSheet),
            messages,
            signals,
            GetSheetOrNull(workbook, DbcWorkbookSchema.ValueDescriptionsSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.MultiplexRangesSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.EnvironmentVariablesSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.AttributeDefinitionsSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.AttributesSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributeDefinitionsSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributeDefaultsSheet),
            GetSheetOrNull(workbook, DbcWorkbookSchema.RelationAttributesSheet));
        return true;
    }

    private static void ValidateOptionalHeaders(SpreadsheetSheet? sheet, IReadOnlyList<string> expectedHeaders, List<DbcDiagnostic> diagnostics)
    {
        if (sheet is not null)
        {
            ValidateHeaders(sheet, expectedHeaders, diagnostics);
        }
    }

    private static void ValidateHeaders(SpreadsheetSheet sheet, IReadOnlyList<string> expectedHeaders, List<DbcDiagnostic> diagnostics)
    {
        foreach (var header in expectedHeaders)
        {
            if (!sheet.Headers.Contains(header, StringComparer.Ordinal))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_MISSING_HEADER", $"Sheet '{sheet.Name}' is missing required header '{header}'."));
            }
        }
    }

    private static bool HasSheet(SpreadsheetWorkbook workbook, string name)
    {
        return workbook.Sheets.Any(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
    }

    private static SpreadsheetSheet? GetSheetOrNull(SpreadsheetWorkbook workbook, string name)
    {
        return workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
    }

    private static IEnumerable<SpreadsheetRow> Rows(SpreadsheetSheet? sheet)
    {
        return sheet?.DataRows ?? [];
    }

    private static string? BuildNetworkComment(SpreadsheetSheet? sheet, List<DbcDiagnostic> diagnostics)
    {
        _ = diagnostics;
        foreach (var row in Rows(sheet))
        {
            if (IsBlankRow(row, DbcWorkbookSchema.NetworkHeaders))
            {
                continue;
            }

            return CleanOptionalQuotedText(row.Get("comment"));
        }

        return null;
    }

    private static Dictionary<string, SpreadsheetRow> BuildMessageRowIndex(SpreadsheetSheet sheet, List<DbcDiagnostic> diagnostics)
    {
        var rows = new Dictionary<string, SpreadsheetRow>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in sheet.DataRows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.MessageHeaders))
            {
                continue;
            }

            var name = row.Get("message_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("message_name")} is required."));
                continue;
            }

            if (IsVectorIndependentMessageName(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_UNSUPPORTED_MESSAGE_NAME", $"{row.CellAddress("message_name")} cannot use Vector independent pseudo message '{VectorIndependentMessageName}'."));
                continue;
            }

            if (!rows.TryAdd(name, row))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_MESSAGE_NAME", $"{row.CellAddress("message_name")} duplicates message '{name}'."));
            }

            var canId = ParseUInt32(row, "can_id", diagnostics);
            var format = ParseOptionalEnum<CanIdFormat>(row, "id_format", diagnostics);
            if (canId is not null && format.HasValue && !ids.Add($"{format.Value}:{canId.Value}"))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_CAN_ID", $"{row.CellAddress("can_id")} duplicates CAN ID '{canId.Value}' with id_format '{format.Value}'."));
            }
        }

        return rows;
    }

    private static SignalRows BuildSignalRowIndex(SpreadsheetSheet sheet, IReadOnlyCollection<string> messageNames, List<DbcDiagnostic> diagnostics)
    {
        var rowsByMessage = new Dictionary<string, List<SpreadsheetRow>>(StringComparer.Ordinal);
        var knownSignals = new HashSet<SignalReference>();
        foreach (var row in sheet.DataRows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.SignalHeaders))
            {
                continue;
            }

            var messageName = row.Get("message_name");
            var signalName = row.Get("signal_name");
            if (string.IsNullOrWhiteSpace(messageName))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("message_name")} is required."));
                continue;
            }

            if (!messageNames.Contains(messageName))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_MESSAGE_NAME", $"{row.CellAddress("message_name")} references unknown message '{messageName}'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(signalName))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("signal_name")} is required."));
                continue;
            }

            var reference = new SignalReference(messageName, signalName);
            if (!knownSignals.Add(reference))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_SIGNAL_NAME", $"{row.CellAddress("signal_name")} duplicates signal '{signalName}' in message '{messageName}'."));
                continue;
            }

            if (!rowsByMessage.TryGetValue(messageName, out var rows))
            {
                rows = [];
                rowsByMessage[messageName] = rows;
            }

            rows.Add(row);
        }

        return new SignalRows(rowsByMessage, knownSignals);
    }

    private static void ValidateValueDescriptionRows(IEnumerable<SpreadsheetRow> rows, IReadOnlySet<SignalReference> knownSignals, List<DbcDiagnostic> diagnostics)
    {
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.ValueDescriptionHeaders))
            {
                continue;
            }

            var reference = new SignalReference(row.Get("message_name"), row.Get("signal_name"));
            if (!knownSignals.Contains(reference))
            {
                diagnostics.Add(Error(
                    "DBC_WORKBOOK_UNKNOWN_SIGNAL_NAME",
                    $"{row.CellAddress("signal_name")} references unknown signal '{reference.SignalName}' in message '{reference.MessageName}'."));
            }
        }
    }

    private static Dictionary<SignalReference, List<MultiplexRangeRow>> BuildMultiplexRangeIndex(
        IEnumerable<SpreadsheetRow> rows,
        IReadOnlySet<SignalReference> knownSignals,
        List<DbcDiagnostic> diagnostics)
    {
        var result = new Dictionary<SignalReference, List<MultiplexRangeRow>>();
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.MultiplexRangeHeaders))
            {
                continue;
            }

            var reference = new SignalReference(row.Get("message_name"), row.Get("signal_name"));
            if (!knownSignals.Contains(reference))
            {
                diagnostics.Add(Error(
                    "DBC_WORKBOOK_UNKNOWN_SIGNAL_NAME",
                    $"{row.CellAddress("signal_name")} references unknown signal '{reference.SignalName}' in message '{reference.MessageName}'."));
                continue;
            }

            var minimum = ParseInt64(row, "range_minimum", diagnostics);
            var maximum = ParseInt64(row, "range_maximum", diagnostics);
            if (minimum is null || maximum is null)
            {
                continue;
            }

            if (minimum.Value < 0 || maximum.Value < minimum.Value)
            {
                diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("range_minimum")} and {row.CellAddress("range_maximum")} must define a non-negative range with minimum <= maximum."));
                continue;
            }

            if (!result.TryGetValue(reference, out var ranges))
            {
                ranges = [];
                result[reference] = ranges;
            }

            ranges.Add(new MultiplexRangeRow(row, row.Get("multiplexor_signal_name"), new DbcMultiplexorRange(minimum.Value, maximum.Value)));
        }

        return result;
    }

    private static Dictionary<string, SpreadsheetRow> BuildEnvironmentVariableRowIndex(IEnumerable<SpreadsheetRow> rows, List<DbcDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, SpreadsheetRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.EnvironmentVariableHeaders))
            {
                continue;
            }

            var name = row.Get("environment_variable_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("environment_variable_name")} is required."));
                continue;
            }

            if (!result.TryAdd(name, row))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_ENVIRONMENT_VARIABLE_NAME", $"{row.CellAddress("environment_variable_name")} duplicates environment variable '{name}'."));
            }
        }

        return result;
    }

    private static Dictionary<string, DbcAttributeDefinition> BuildAttributeDefinitions(IEnumerable<SpreadsheetRow> rows, List<DbcDiagnostic> diagnostics)
    {
        var definitions = new Dictionary<string, DbcAttributeDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.AttributeDefinitionHeaders))
            {
                continue;
            }

            var name = row.Get("attribute_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("attribute_name")} is required."));
                continue;
            }

            if (IsWorkbookManagedAttribute(name))
            {
                continue;
            }

            if (!Enum.TryParse<DbcAttributeOwnerKind>(row.Get("owner_type"), ignoreCase: true, out var ownerKind))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("owner_type")} has unsupported owner_type '{row.Get("owner_type")}'."));
                continue;
            }

            if (!Enum.TryParse<DbcAttributeValueKind>(row.Get("value_kind"), ignoreCase: true, out var valueKind))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("value_kind")} has unsupported value kind '{row.Get("value_kind")}'."));
                continue;
            }

            var enumValues = ParseList(row.Get("enum_values"));
            var minimum = ParseNullableDouble(row, "minimum", diagnostics);
            var maximum = ParseNullableDouble(row, "maximum", diagnostics);
            var defaultRawValue = row.Get("default_raw_value");
            DbcAttributeValue? defaultValue = string.IsNullOrEmpty(defaultRawValue)
                ? null
                : CreateAttributeValue(name, valueKind, defaultRawValue, enumValues);

            if (!definitions.TryAdd(name, new DbcAttributeDefinition(name, ownerKind, valueKind, enumValues, minimum, maximum, defaultValue)))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_ATTRIBUTE_DEFINITION", $"{row.CellAddress("attribute_name")} duplicates attribute definition '{name}'."));
            }
        }

        return definitions;
    }

    private static Dictionary<string, DbcRelationAttributeDefinition> BuildRelationAttributeDefinitions(IEnumerable<SpreadsheetRow> rows, List<DbcDiagnostic> diagnostics)
    {
        var definitions = new Dictionary<string, DbcRelationAttributeDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.RelationAttributeDefinitionHeaders))
            {
                continue;
            }

            var name = row.Get("attribute_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("attribute_name")} is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Get("relation_kind")))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("relation_kind")} is required."));
                continue;
            }

            if (!Enum.TryParse<DbcAttributeValueKind>(row.Get("value_kind"), ignoreCase: true, out var valueKind))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("value_kind")} has unsupported value kind '{row.Get("value_kind")}'."));
                continue;
            }

            var enumValues = ParseList(row.Get("enum_values"));
            var minimum = ParseNullableDouble(row, "minimum", diagnostics);
            var maximum = ParseNullableDouble(row, "maximum", diagnostics);
            if (!definitions.TryAdd(name, new DbcRelationAttributeDefinition(name, row.Get("relation_kind"), valueKind, enumValues, minimum, maximum)))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_RELATION_ATTRIBUTE_DEFINITION", $"{row.CellAddress("attribute_name")} duplicates relation attribute definition '{name}'."));
            }
        }

        return definitions;
    }

    private static Dictionary<string, DbcRelationAttributeDefault> BuildRelationAttributeDefaults(
        IEnumerable<SpreadsheetRow> rows,
        IReadOnlyDictionary<string, DbcRelationAttributeDefinition> definitions,
        List<DbcDiagnostic> diagnostics)
    {
        var defaults = new Dictionary<string, DbcRelationAttributeDefault>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.RelationAttributeDefaultHeaders))
            {
                continue;
            }

            var name = row.Get("attribute_name");
            if (!definitions.ContainsKey(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_RELATION_ATTRIBUTE_DEFINITION", $"{row.CellAddress("attribute_name")} references unknown relation attribute definition '{name}'."));
                continue;
            }

            defaults[name] = new DbcRelationAttributeDefault(name, row.Get("raw_value"));
        }

        return defaults;
    }

    private static List<DbcRelationAttributeValue> BuildRelationAttributes(
        IEnumerable<SpreadsheetRow> rows,
        IReadOnlyDictionary<string, DbcRelationAttributeDefinition> definitions,
        List<DbcDiagnostic> diagnostics)
    {
        var values = new List<DbcRelationAttributeValue>();
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.RelationAttributeHeaders))
            {
                continue;
            }

            var name = row.Get("attribute_name");
            if (!definitions.ContainsKey(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_RELATION_ATTRIBUTE_DEFINITION", $"{row.CellAddress("attribute_name")} references unknown relation attribute definition '{name}'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Get("target")))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("target")} is required."));
                continue;
            }

            values.Add(new DbcRelationAttributeValue(name, row.Get("target"), row.Get("raw_value")));
        }

        return values;
    }

    private static void ApplyAttributeRows(
        IEnumerable<SpreadsheetRow> rows,
        IReadOnlyCollection<string> messageNames,
        IReadOnlySet<SignalReference> signalReferences,
        IReadOnlyCollection<string> environmentVariableNames,
        Dictionary<string, DbcAttributeDefinition> attributeDefinitions,
        Dictionary<string, DbcAttributeValue> networkAttributes,
        Dictionary<string, Dictionary<string, DbcAttributeValue>> nodeAttributes,
        Dictionary<string, Dictionary<string, DbcAttributeValue>> messageAttributes,
        Dictionary<SignalReference, Dictionary<string, DbcAttributeValue>> signalAttributes,
        Dictionary<string, Dictionary<string, DbcAttributeValue>> environmentVariableAttributes,
        List<DbcDiagnostic> diagnostics)
    {
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.AttributeHeaders))
            {
                continue;
            }

            var attributeName = row.Get("attribute_name");
            if (string.IsNullOrWhiteSpace(attributeName))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("attribute_name")} is required."));
                continue;
            }

            if (IsWorkbookManagedAttribute(attributeName))
            {
                continue;
            }

            if (!attributeDefinitions.TryGetValue(attributeName, out var definition))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_ATTRIBUTE_DEFINITION", $"{row.CellAddress("attribute_name")} references unknown attribute definition '{attributeName}'."));
                continue;
            }

            var value = CreateAttributeValue(attributeName, definition.ValueKind, row.Get("raw_value"), definition.EnumValues);
            var ownerType = row.Get("owner_type");
            switch (ownerType)
            {
                case "Network":
                    networkAttributes[attributeName] = value;
                    break;
                case "Node":
                    AddAttribute(GetOverlay(nodeAttributes, row.Get("node_name")), value);
                    break;
                case "Message":
                    if (!messageNames.Contains(row.Get("message_name")))
                    {
                        diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_MESSAGE_NAME", $"{row.CellAddress("message_name")} references unknown message '{row.Get("message_name")}'."));
                        break;
                    }

                    AddAttribute(GetOverlay(messageAttributes, row.Get("message_name")), value);
                    break;
                case "Signal":
                    var reference = new SignalReference(row.Get("message_name"), row.Get("signal_name"));
                    if (!signalReferences.Contains(reference))
                    {
                        diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_SIGNAL_NAME", $"{row.CellAddress("signal_name")} references unknown signal '{reference.SignalName}' in message '{reference.MessageName}'."));
                        break;
                    }

                    AddAttribute(GetOverlay(signalAttributes, reference), value);
                    break;
                case "EnvironmentVariable":
                    var variableName = row.Get("environment_variable_name");
                    if (!environmentVariableNames.Contains(variableName))
                    {
                        diagnostics.Add(Error("DBC_WORKBOOK_UNKNOWN_ENVIRONMENT_VARIABLE_NAME", $"{row.CellAddress("environment_variable_name")} references unknown environment variable '{variableName}'."));
                        break;
                    }

                    AddAttribute(GetOverlay(environmentVariableAttributes, variableName), value);
                    break;
                default:
                    diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("owner_type")} has unsupported owner_type '{ownerType}'."));
                    break;
            }
        }
    }

    private static Dictionary<string, DbcNode> BuildNodes(
        IEnumerable<SpreadsheetRow> rows,
        IReadOnlyDictionary<string, Dictionary<string, DbcAttributeValue>> attributeRows,
        List<DbcDiagnostic> diagnostics)
    {
        var nodes = new Dictionary<string, DbcNode>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.NodeHeaders))
            {
                continue;
            }

            var name = row.Get("node_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("node_name")} is required."));
                continue;
            }

            if (IsEmptyNodeName(name))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_RESERVED_NODE_NAME", $"{row.CellAddress("node_name")} cannot use reserved node name Vector__XXX."));
                continue;
            }

            var attributes = attributeRows.TryGetValue(name, out var overlay)
                ? overlay
                : new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
            if (!nodes.TryAdd(name, new DbcNode(name, CleanOptionalQuotedText(row.Get("comment")), attributes)))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_NODE_NAME", $"{row.CellAddress("node_name")} duplicates node '{name}'."));
            }
        }

        return nodes;
    }

    private static Dictionary<string, DbcEnvironmentVariable> BuildEnvironmentVariables(
        IReadOnlyDictionary<string, SpreadsheetRow> environmentRows,
        IReadOnlyDictionary<string, Dictionary<string, DbcAttributeValue>> attributeRows,
        Dictionary<string, DbcNode> nodesByName,
        List<DbcDiagnostic> diagnostics)
    {
        var variables = new Dictionary<string, DbcEnvironmentVariable>(StringComparer.Ordinal);
        foreach (var (name, row) in environmentRows)
        {
            var valueType = ParseInt32(row, "value_type", diagnostics) ?? 0;
            var minimum = ParseDouble(row, "minimum", diagnostics) ?? 0;
            var maximum = ParseDouble(row, "maximum", diagnostics) ?? 0;
            var initialValue = ParseDouble(row, "initial_value", diagnostics) ?? 0;
            var identifier = ParseInt32(row, "identifier", diagnostics) ?? 0;
            var accessType = string.IsNullOrWhiteSpace(row.Get("access_type")) ? "DUMMY_NODE_VECTOR0" : row.Get("access_type");
            var accessNodes = ParseNodeList(row.Get("access_nodes")).Select(nodeName => GetOrCreateNode(nodesByName, nodeName)).ToArray();
            var attributes = attributeRows.TryGetValue(name, out var overlay)
                ? overlay
                : new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
            variables[name] = new DbcEnvironmentVariable(
                name,
                valueType,
                minimum,
                maximum,
                CleanQuotedText(row.Get("unit")),
                initialValue,
                identifier,
                accessType,
                accessNodes,
                attributes: attributes);
        }

        return variables;
    }

    private static DbcMessage? BuildMessage(
        SpreadsheetRow row,
        IReadOnlyList<SpreadsheetRow> signalRows,
        IEnumerable<SpreadsheetRow> valueDescriptionRows,
        IReadOnlyDictionary<SignalReference, List<MultiplexRangeRow>> multiplexRanges,
        IReadOnlyDictionary<string, Dictionary<string, DbcAttributeValue>> messageAttributeRows,
        IReadOnlyDictionary<SignalReference, Dictionary<string, DbcAttributeValue>> signalAttributeRows,
        Dictionary<string, DbcAttributeDefinition> attributeDefinitions,
        Dictionary<string, DbcNode> nodesByName,
        List<DbcDiagnostic> diagnostics)
    {
        var name = row.Get("message_name");
        var dataLength = ParseInt32(row, "dlc", diagnostics) ?? 8;
        var rawId = ParseMessageRawId(row, diagnostics) ?? new DbcRawMessageId(0);
        var transmitters = ParseNodeList(row.Get("transmitters"));
        var cycleTimeMs = ParseNullableInt32(row, "cycle_time_ms", diagnostics);
        var timeoutTimeMs = ParseNullableInt32(row, "timeout_ms", diagnostics);
        var sendType = ParseOptionalEnum<DbcSendType>(row, "send_type", diagnostics) ?? DbcSendType.Unknown;
        var frameFlags = ParseBoolean(row.Get("is_can_fd")) ? DbcFrameFlags.FlexibleDataRate : DbcFrameFlags.None;

        var attributes = messageAttributeRows.TryGetValue(name, out var overlay)
            ? new Dictionary<string, DbcAttributeValue>(overlay, StringComparer.Ordinal)
            : new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
        if (cycleTimeMs.HasValue)
        {
            EnsureIntegerDefinition(attributeDefinitions, "GenMsgCycleTime", DbcAttributeOwnerKind.Message);
            attributes["GenMsgCycleTime"] = IntAttribute("GenMsgCycleTime", cycleTimeMs.Value);
        }

        if (timeoutTimeMs.HasValue)
        {
            EnsureIntegerDefinition(attributeDefinitions, "GenMsgTimeoutTime", DbcAttributeOwnerKind.Message);
            attributes["GenMsgTimeoutTime"] = IntAttribute("GenMsgTimeoutTime", timeoutTimeMs.Value);
        }

        if (sendType != DbcSendType.Unknown)
        {
            EnsureSendTypeDefinition(attributeDefinitions, "GenMsgSendType", DbcAttributeOwnerKind.Message);
            attributes["GenMsgSendType"] = SendTypeAttribute("GenMsgSendType", sendType);
        }

        if ((frameFlags & DbcFrameFlags.FlexibleDataRate) != 0 && dataLength <= 8)
        {
            EnsureVFrameFormatDefinition(attributeDefinitions);
            var isExtended = rawId.ToCanIdentifier().Format == CanIdFormat.Extended;
            attributes["VFrameFormat"] = new DbcAttributeValue(
                "VFrameFormat",
                DbcAttributeValueKind.Enum,
                isExtended ? "15" : "14",
                isExtended ? "ExtendedCAN_FD" : "StandardCAN_FD");
        }

        var signals = new List<DbcSignal>();
        foreach (var signalRow in signalRows)
        {
            var signal = BuildSignal(signalRow, valueDescriptionRows, multiplexRanges, signalAttributeRows, attributeDefinitions, nodesByName, diagnostics);
            if (signal is not null)
            {
                signals.Add(signal);
            }
        }

        var primaryTransmitter = transmitters.Count == 0
            ? new DbcNode(EmptyNodeName)
            : GetOrCreateNode(nodesByName, transmitters[0]);

        return diagnostics.Any(IsError)
            ? null
            : new DbcMessage(
                rawId,
                name,
                dataLength,
                primaryTransmitter,
                signals,
                transmitters.Select(nodeName => GetOrCreateNode(nodesByName, nodeName)).ToArray(),
                attributes,
                CleanOptionalQuotedText(row.Get("comment")),
                cycleTimeMs,
                frameFlags,
                sendType: sendType,
                timeoutTimeMs: timeoutTimeMs);
    }

    private static DbcSignal? BuildSignal(
        SpreadsheetRow row,
        IEnumerable<SpreadsheetRow> valueDescriptionRows,
        IReadOnlyDictionary<SignalReference, List<MultiplexRangeRow>> multiplexRanges,
        IReadOnlyDictionary<SignalReference, Dictionary<string, DbcAttributeValue>> signalAttributeRows,
        Dictionary<string, DbcAttributeDefinition> attributeDefinitions,
        Dictionary<string, DbcNode> nodesByName,
        List<DbcDiagnostic> diagnostics)
    {
        var reference = new SignalReference(row.Get("message_name"), row.Get("signal_name"));
        var attributes = signalAttributeRows.TryGetValue(reference, out var overlay)
            ? new Dictionary<string, DbcAttributeValue>(overlay, StringComparer.Ordinal)
            : new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
        var initialValue = ParseNullableDouble(row, "initial_value", diagnostics);
        var sendType = ParseOptionalEnum<DbcSendType>(row, "send_type", diagnostics) ?? DbcSendType.Unknown;
        var timeoutTimeMs = ParseNullableInt32(row, "timeout_ms", diagnostics);
        if (initialValue.HasValue)
        {
            EnsureFloatDefinition(attributeDefinitions, "GenSigStartValue", DbcAttributeOwnerKind.Signal);
            attributes["GenSigStartValue"] = FloatAttribute("GenSigStartValue", initialValue.Value);
        }

        if (sendType != DbcSendType.Unknown)
        {
            EnsureSendTypeDefinition(attributeDefinitions, "GenSigSendType", DbcAttributeOwnerKind.Signal);
            attributes["GenSigSendType"] = SendTypeAttribute("GenSigSendType", sendType);
        }

        if (timeoutTimeMs.HasValue)
        {
            EnsureIntegerDefinition(attributeDefinitions, "GenSigTimeoutTime", DbcAttributeOwnerKind.Signal);
            attributes["GenSigTimeoutTime"] = IntAttribute("GenSigTimeoutTime", timeoutTimeMs.Value);
        }

        var valueDescriptions = BuildValueDescriptions(reference, valueDescriptionRows, diagnostics);
        var receiverNames = ParseNodeList(row.Get("receivers"));
        var receivers = receiverNames.Select(name => GetOrCreateNode(nodesByName, name)).ToArray();
        var multiplexing = BuildMultiplexing(row, reference, multiplexRanges, diagnostics);
        var startBit = ParseInt32(row, "start_bit", diagnostics) ?? 0;
        var bitLength = ParseInt32(row, "length", diagnostics) ?? 1;
        var byteOrder = ParseRequiredEnum<DbcByteOrder>(row, "byte_order", diagnostics);
        var valueType = ParseRequiredEnum<DbcSignalValueType>(row, "value_type", diagnostics);
        var factor = ParseDouble(row, "factor", diagnostics) ?? 1;
        var offset = ParseDouble(row, "offset", diagnostics) ?? 0;
        var minimum = ParseDouble(row, "minimum", diagnostics) ?? 0;
        var maximum = ParseDouble(row, "maximum", diagnostics) ?? 0;

        return diagnostics.Any(IsError)
            ? null
            : new DbcSignal(
                row.Get("signal_name"),
                startBit,
                bitLength,
                byteOrder!.Value,
                valueType!.Value,
                factor,
                offset,
                minimum,
                maximum,
                CleanQuotedText(row.Get("unit")),
                receivers,
                multiplexing,
                valueDescriptions: valueDescriptions,
                attributes: attributes,
                comment: CleanOptionalQuotedText(row.Get("comment")),
                initialValue: initialValue,
                sendType: sendType,
                timeoutTimeMs: timeoutTimeMs);
    }

    private static DbcMultiplexing BuildMultiplexing(
        SpreadsheetRow row,
        SignalReference reference,
        IReadOnlyDictionary<SignalReference, List<MultiplexRangeRow>> rangesBySignal,
        List<DbcDiagnostic> diagnostics)
    {
        var hasRanges = rangesBySignal.TryGetValue(reference, out var rangeRows) && rangeRows.Count > 0;
        var roleText = row.Get("multiplex_role");
        if (string.IsNullOrWhiteSpace(roleText) && hasRanges)
        {
            roleText = DbcMultiplexingRole.Multiplexed.ToString();
        }

        if (string.IsNullOrWhiteSpace(roleText) ||
            Enum.TryParse<DbcMultiplexingRole>(roleText, ignoreCase: true, out var role) && role == DbcMultiplexingRole.None)
        {
            return DbcMultiplexing.None;
        }

        if (!Enum.TryParse<DbcMultiplexingRole>(roleText, ignoreCase: true, out role))
        {
            diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("multiplex_role")} has unsupported multiplex_role '{roleText}'."));
            return DbcMultiplexing.None;
        }

        if (role == DbcMultiplexingRole.Multiplexor)
        {
            return DbcMultiplexing.Multiplexor;
        }

        var switchValue = ParseNullableInt32(row, "multiplex_switch_value", diagnostics);
        var multiplexorName = row.Get("multiplexor_signal_name");
        if (hasRanges)
        {
            var hasRangeNameError = false;
            var rangeMultiplexorNames = rangeRows!
                .Where(rangeRow => !string.IsNullOrWhiteSpace(rangeRow.MultiplexorSignalName))
                .Select(rangeRow => rangeRow.MultiplexorSignalName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (string.IsNullOrWhiteSpace(multiplexorName))
            {
                if (rangeMultiplexorNames.Length == 1)
                {
                    multiplexorName = rangeMultiplexorNames[0];
                }
                else
                {
                    diagnostics.Add(Error(
                        "DBC_WORKBOOK_REQUIRED_CELL",
                        $"{row.CellAddress("multiplexor_signal_name")} is required when MultiplexRanges rows do not define exactly one multiplexor_signal_name."));
                    return DbcMultiplexing.None;
                }
            }
            else
            {
                foreach (var rangeRow in rangeRows!.Where(rangeRow =>
                    !string.IsNullOrWhiteSpace(rangeRow.MultiplexorSignalName) &&
                    !string.Equals(rangeRow.MultiplexorSignalName, multiplexorName, StringComparison.Ordinal)))
                {
                    diagnostics.Add(Error(
                        "DBC_WORKBOOK_INVALID_CELL",
                        $"{rangeRow.Row.CellAddress("multiplexor_signal_name")} must match Signals row multiplexor_signal_name '{multiplexorName}'."));
                    hasRangeNameError = true;
                }
            }

            if (hasRangeNameError)
            {
                return DbcMultiplexing.None;
            }

            var ranges = rangeRows!.Select(rangeRow => rangeRow.Range).ToArray();
            return switchValue.HasValue
                ? DbcMultiplexing.Multiplexed(switchValue.Value).WithExtendedRanges(multiplexorName, ranges!)
                : DbcMultiplexing.Multiplexed(multiplexorName, ranges!);
        }

        if (!switchValue.HasValue)
        {
            diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress("multiplex_switch_value")} is required for basic multiplexed signals."));
            return DbcMultiplexing.None;
        }

        return DbcMultiplexing.Multiplexed(switchValue.Value);
    }

    private static Dictionary<long, string> BuildValueDescriptions(SignalReference reference, IEnumerable<SpreadsheetRow> rows, List<DbcDiagnostic> diagnostics)
    {
        var result = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            if (IsBlankRow(row, DbcWorkbookSchema.ValueDescriptionHeaders) ||
                !string.Equals(row.Get("message_name"), reference.MessageName, StringComparison.Ordinal) ||
                !string.Equals(row.Get("signal_name"), reference.SignalName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!long.TryParse(row.Get("raw_value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("raw_value")} must be an integer, actual '{row.Get("raw_value")}'."));
                continue;
            }

            if (!result.TryAdd(rawValue, CleanQuotedText(row.Get("description"))))
            {
                diagnostics.Add(Error("DBC_WORKBOOK_DUPLICATE_VALUE_DESCRIPTION", $"{row.CellAddress("raw_value")} duplicates value description '{rawValue}'."));
            }
        }

        return result;
    }

    private static DbcRawMessageId? ParseMessageRawId(SpreadsheetRow row, List<DbcDiagnostic> diagnostics)
    {
        var canId = ParseUInt32(row, "can_id", diagnostics);
        if (canId is null)
        {
            return null;
        }

        if (!Enum.TryParse<CanIdFormat>(row.Get("id_format"), ignoreCase: true, out var format))
        {
            diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress("id_format")} has unsupported id_format '{row.Get("id_format")}'."));
            return null;
        }

        return new DbcRawMessageId(format == CanIdFormat.Extended
            ? canId.Value | DbcRawMessageId.ExtendedFrameFlag
            : canId.Value);
    }

    private static uint? ParseUInt32(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        var value = row.Get(header);
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress(header)} is required."));
            return null;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^1];
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return hex;
        }

        if (uint.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress(header)} must be an unsigned integer, actual '{value}'."));
        return null;
    }

    private static int? ParseInt32(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        var value = row.Get(header);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress(header)} must be an integer, actual '{value}'."));
        return null;
    }

    private static int? ParseNullableInt32(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        return string.IsNullOrWhiteSpace(row.Get(header)) ? null : ParseInt32(row, header, diagnostics);
    }

    private static long? ParseInt64(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        var value = row.Get(header);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress(header)} must be an integer, actual '{value}'."));
        return null;
    }

    private static double? ParseDouble(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        var value = row.Get(header);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress(header)} must be a finite number, actual '{value}'."));
        return null;
    }

    private static double? ParseNullableDouble(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
    {
        return string.IsNullOrWhiteSpace(row.Get(header)) ? null : ParseDouble(row, header, diagnostics);
    }

    private static TEnum? ParseOptionalEnum<TEnum>(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
        where TEnum : struct
    {
        var value = row.Get(header);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error("DBC_WORKBOOK_INVALID_CELL", $"{row.CellAddress(header)} has unsupported {header} '{value}'."));
        return null;
    }

    private static TEnum? ParseRequiredEnum<TEnum>(SpreadsheetRow row, string header, List<DbcDiagnostic> diagnostics)
        where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(row.Get(header)))
        {
            diagnostics.Add(Error("DBC_WORKBOOK_REQUIRED_CELL", $"{row.CellAddress(header)} is required."));
            return null;
        }

        return ParseOptionalEnum<TEnum>(row, header, diagnostics);
    }

    private static bool ParseBoolean(string value)
    {
        return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ParseNodeList(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsEmptyNodeName(value))
        {
            return [];
        }

        return ParseList(value).Where(name => !IsEmptyNodeName(name)).ToArray();
    }

    private static IReadOnlyList<string> ParseList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static DbcNode GetOrCreateNode(Dictionary<string, DbcNode> nodesByName, string name)
    {
        if (nodesByName.TryGetValue(name, out var node))
        {
            return node;
        }

        node = new DbcNode(name);
        nodesByName[name] = node;
        return node;
    }

    private static Dictionary<string, DbcAttributeValue> GetOverlay(Dictionary<string, Dictionary<string, DbcAttributeValue>> overlays, string key)
    {
        if (!overlays.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
            overlays[key] = values;
        }

        return values;
    }

    private static Dictionary<string, DbcAttributeValue> GetOverlay(Dictionary<SignalReference, Dictionary<string, DbcAttributeValue>> overlays, SignalReference key)
    {
        if (!overlays.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
            overlays[key] = values;
        }

        return values;
    }

    private static void AddAttribute(Dictionary<string, DbcAttributeValue> attributes, DbcAttributeValue attribute)
    {
        attributes[attribute.Name] = attribute;
    }

    private static DbcAttributeValue CreateAttributeValue(
        string name,
        DbcAttributeValueKind valueKind,
        string rawValue,
        IReadOnlyList<string> enumValues)
    {
        rawValue = valueKind is DbcAttributeValueKind.String or DbcAttributeValueKind.Enum
            ? CleanQuotedText(rawValue)
            : rawValue;
        object? value = valueKind switch
        {
            DbcAttributeValueKind.Integer when long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            DbcAttributeValueKind.Hex when TryParseHexOrDecimalInteger(rawValue, out var hex) => hex,
            DbcAttributeValueKind.Float when double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            DbcAttributeValueKind.Enum when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < enumValues.Count => enumValues[index],
            DbcAttributeValueKind.Enum when enumValues.Contains(rawValue) => rawValue,
            _ => rawValue,
        };

        return new DbcAttributeValue(name, valueKind, rawValue, value);
    }

    private static void EnsureIntegerDefinition(Dictionary<string, DbcAttributeDefinition> definitions, string name, DbcAttributeOwnerKind ownerKind)
    {
        if (!definitions.ContainsKey(name))
        {
            definitions[name] = new DbcAttributeDefinition(name, ownerKind, DbcAttributeValueKind.Integer, minimum: 0, maximum: int.MaxValue);
        }
    }

    private static void EnsureFloatDefinition(Dictionary<string, DbcAttributeDefinition> definitions, string name, DbcAttributeOwnerKind ownerKind)
    {
        if (!definitions.ContainsKey(name))
        {
            definitions[name] = new DbcAttributeDefinition(name, ownerKind, DbcAttributeValueKind.Float, minimum: 0, maximum: double.MaxValue);
        }
    }

    private static void EnsureSendTypeDefinition(Dictionary<string, DbcAttributeDefinition> definitions, string name, DbcAttributeOwnerKind ownerKind)
    {
        if (!definitions.ContainsKey(name))
        {
            var none = string.Equals(name, "GenSigSendType", StringComparison.Ordinal) ? "NoSigSendType" : "NoMsgSendType";
            definitions[name] = new DbcAttributeDefinition(
                name,
                ownerKind,
                DbcAttributeValueKind.Enum,
                [none, "Cyclic", "Event", "CyclicIfActive", "CyclicAndEvent", "IfActive", "OnWrite", "OnWriteWithRepetition", "OnChange", "OnChangeWithRepetition", "IfActiveWithRepetition"]);
        }
    }

    private static void EnsureVFrameFormatDefinition(Dictionary<string, DbcAttributeDefinition> definitions)
    {
        if (!definitions.ContainsKey("VFrameFormat"))
        {
            definitions["VFrameFormat"] = new DbcAttributeDefinition(
                "VFrameFormat",
                DbcAttributeOwnerKind.Message,
                DbcAttributeValueKind.Enum,
                ["StandardCAN", "ExtendedCAN", "reserved", "J1939PG", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "StandardCAN_FD", "ExtendedCAN_FD"]);
        }
    }

    private static DbcAttributeValue IntAttribute(string name, int value)
    {
        return new DbcAttributeValue(name, DbcAttributeValueKind.Integer, value.ToString(CultureInfo.InvariantCulture), value);
    }

    private static DbcAttributeValue FloatAttribute(string name, double value)
    {
        return new DbcAttributeValue(name, DbcAttributeValueKind.Float, value.ToString("G17", CultureInfo.InvariantCulture), value);
    }

    private static DbcAttributeValue SendTypeAttribute(string name, DbcSendType value)
    {
        var token = SendTypeToken(name, value);
        return new DbcAttributeValue(name, DbcAttributeValueKind.Enum, token, token);
    }

    private static string SendTypeToken(string attributeName, DbcSendType value)
    {
        return value switch
        {
            DbcSendType.None => string.Equals(attributeName, "GenSigSendType", StringComparison.Ordinal) ? "NoSigSendType" : "NoMsgSendType",
            DbcSendType.Cyclic => "Cyclic",
            DbcSendType.Event => "Event",
            DbcSendType.CyclicIfActive => "CyclicIfActive",
            DbcSendType.CyclicAndEvent => "CyclicAndEvent",
            DbcSendType.IfActive => "IfActive",
            DbcSendType.OnWrite => "OnWrite",
            DbcSendType.OnWriteWithRepetition => "OnWriteWithRepetition",
            DbcSendType.OnChange => "OnChange",
            DbcSendType.OnChangeWithRepetition => "OnChangeWithRepetition",
            DbcSendType.IfActiveWithRepetition => "IfActiveWithRepetition",
            _ => string.Equals(attributeName, "GenSigSendType", StringComparison.Ordinal) ? "NoSigSendType" : "NoMsgSendType",
        };
    }

    private static bool IsBlankRow(SpreadsheetRow row, IReadOnlyList<string> headers)
    {
        return headers.All(header => string.IsNullOrWhiteSpace(row.Get(header)));
    }

    private static bool IsError(DbcDiagnostic diagnostic)
    {
        return diagnostic.Severity == DbcDiagnosticSeverity.Error;
    }

    private static string? CleanOptionalQuotedText(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : CleanQuotedText(value);
    }

    private static string CleanQuotedText(string value)
    {
        if (value.Length == 0 || !value.Any(char.IsControl))
        {
            return value;
        }

        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (char.IsControl(buffer[i]))
            {
                buffer[i] = ' ';
            }
        }

        return new string(buffer);
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

    private static bool TryParseHexOrDecimalInteger(string rawValue, out ulong value)
    {
        if (rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(rawValue[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsVectorIndependentMessageName(string name)
    {
        return string.Equals(name, VectorIndependentMessageName, StringComparison.Ordinal);
    }

    private static DbcDiagnostic Error(string code, string message)
    {
        return new DbcDiagnostic(DbcDiagnosticSeverity.Error, code, message);
    }

    private readonly record struct SignalReference(string MessageName, string SignalName);

    private sealed record MultiplexRangeRow(SpreadsheetRow Row, string MultiplexorSignalName, DbcMultiplexorRange Range);

    private sealed record SignalRows(Dictionary<string, List<SpreadsheetRow>> RowsByMessage, HashSet<SignalReference> KnownSignals);

    private sealed record WorkbookSheets(
        SpreadsheetSheet? Network,
        SpreadsheetSheet? Nodes,
        SpreadsheetSheet Messages,
        SpreadsheetSheet Signals,
        SpreadsheetSheet? ValueDescriptions,
        SpreadsheetSheet? MultiplexRanges,
        SpreadsheetSheet? EnvironmentVariables,
        SpreadsheetSheet? AttributeDefinitions,
        SpreadsheetSheet? Attributes,
        SpreadsheetSheet? RelationAttributeDefinitions,
        SpreadsheetSheet? RelationAttributeDefaults,
        SpreadsheetSheet? RelationAttributes);
}
