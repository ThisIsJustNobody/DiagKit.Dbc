using System.Globalization;
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
            builder.Append("    EV_DATA_").Append(newline);
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
        AppendMessages(builder, document, options, newline);
        AppendAdditionalTransmitters(builder, document, options, newline);
        AppendEnvironmentVariables(builder, document, options, newline);
        AppendMetadata(builder, document, options, newline);
        return builder.ToString();
    }

    private static IEnumerable<DbcNode> GetNodes(DbcDocument document, DbcWriterOptions options)
    {
        return options.SortMode == DbcWriterSortMode.Stable
            ? document.Nodes.OrderBy(node => DbcWriterNameFormatter.GetNodeExportName(node, options), StringComparer.Ordinal)
            : document.Nodes;
    }

    private static IEnumerable<DbcMessage> EnumerateMessages(DbcDocument document, DbcWriterOptions options)
    {
        return options.SortMode == DbcWriterSortMode.Stable
            ? document.Messages
                .OrderBy(message => message.RawId.Value)
                .ThenBy(message => DbcWriterNameFormatter.GetMessageExportName(message, options), StringComparer.Ordinal)
            : document.Messages;
    }

    private static IEnumerable<DbcSignal> EnumerateSignals(DbcMessage message, DbcWriterOptions options)
    {
        return options.SortMode == DbcWriterSortMode.Stable
            ? message.Signals
                .OrderBy(signal => DbcWriterNameFormatter.GetSignalExportName(signal, options), StringComparer.Ordinal)
                .ThenBy(signal => signal.StartBit)
            : message.Signals;
    }

    private static void AppendMessages(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        var hasAnyMessage = false;
        foreach (var message in EnumerateMessages(document, options))
        {
            if (!hasAnyMessage)
            {
                builder.Append(newline);
                hasAnyMessage = true;
            }

            builder.Append("BO_ ")
                .Append(message.RawId.Value)
                .Append(' ')
                .Append(DbcWriterNameFormatter.GetMessageExportName(message, options))
                .Append(": ")
                .Append(message.DataLength)
                .Append(' ')
                .Append(DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options))
                .Append(newline);

            foreach (var signal in EnumerateSignals(message, options))
            {
                AppendSignal(builder, signal, options, newline);
            }

            builder.Append(newline);
        }
    }

    private static void AppendSignal(StringBuilder builder, DbcSignal signal, DbcWriterOptions options, string newline)
    {
        builder.Append(" SG_ ")
            .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options))
            .Append(GetMultiplexingToken(signal.Multiplexing))
            .Append(" : ")
            .Append(signal.StartBit)
            .Append('|')
            .Append(signal.BitLength)
            .Append('@')
            .Append(signal.ByteOrder == DbcByteOrder.Intel ? '1' : '0')
            .Append(signal.ValueType == DbcSignalValueType.Signed ? '-' : '+')
            .Append(" (")
            .Append(FormatNumber(signal.Factor))
            .Append(',')
            .Append(FormatNumber(signal.Offset))
            .Append(") [")
            .Append(FormatNumber(signal.Minimum))
            .Append('|')
            .Append(FormatNumber(signal.Maximum))
            .Append("] \"")
            .Append(EscapeQuotedText(signal.Unit))
            .Append("\" ")
            .Append(FormatReceivers(signal.Receivers, options))
            .Append(newline);
    }

    private static void AppendMetadata(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        AppendComments(builder, document, options, newline);
        AppendAttributeDefinitions(builder, document, newline);
        AppendAttributeDefaults(builder, document, newline);
        AppendAttributeValues(builder, document, options, newline);
        AppendRelationAttributes(builder, document, newline);
        AppendValueDescriptions(builder, document, options, newline);
        AppendSignalValueTypes(builder, document, options, newline);
        AppendExtendedMultiplexing(builder, document, options, newline);
    }

    private static void AppendAdditionalTransmitters(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var message in EnumerateMessages(document, options))
        {
            var primaryName = DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options);
            var additionalTransmitters = message.Transmitters
                .Select(transmitter => DbcWriterNameFormatter.GetNodeExportName(transmitter, options))
                .Where(transmitterName => !string.Equals(transmitterName, primaryName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (additionalTransmitters.Length == 0)
            {
                continue;
            }

            builder.Append("BO_TX_BU_ ")
                .Append(message.RawId.Value)
                .Append(" : ")
                .Append(string.Join(",", additionalTransmitters))
                .Append(';')
                .Append(newline);
        }
    }

    private static void AppendEnvironmentVariables(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var variable in EnumerateEnvironmentVariables(document, options))
        {
            builder.Append("EV_ ")
                .Append(DbcWriterNameFormatter.GetEnvironmentVariableExportName(variable, options))
                .Append(" : ")
                .Append(variable.ValueType.ToString(CultureInfo.InvariantCulture))
                .Append(" [")
                .Append(FormatNumber(variable.Minimum))
                .Append('|')
                .Append(FormatNumber(variable.Maximum))
                .Append("] \"")
                .Append(EscapeQuotedText(variable.Unit))
                .Append("\" ")
                .Append(FormatNumber(variable.InitialValue))
                .Append(' ')
                .Append(variable.Identifier.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(variable.AccessType);

            if (variable.AccessNodes.Count > 0)
            {
                builder.Append(' ')
                    .Append(string.Join(",", variable.AccessNodes.Select(node => DbcWriterNameFormatter.GetNodeExportName(node, options))));
            }

            builder.Append(';').Append(newline);
        }
    }

    private static IEnumerable<DbcEnvironmentVariable> EnumerateEnvironmentVariables(DbcDocument document, DbcWriterOptions options)
    {
        return options.SortMode == DbcWriterSortMode.Stable
            ? document.EnvironmentVariables.Values.OrderBy(variable => DbcWriterNameFormatter.GetEnvironmentVariableExportName(variable, options), StringComparer.Ordinal)
            : document.EnvironmentVariables.Values;
    }

    private static void AppendAttributeDefinitions(StringBuilder builder, DbcDocument document, string newline)
    {
        foreach (var definition in document.AttributeDefinitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            builder.Append("BA_DEF_ ");
            var ownerToken = GetOwnerToken(definition.OwnerKind);
            if (ownerToken.Length > 0)
            {
                builder.Append(ownerToken).Append(' ');
            }

            builder.Append('"')
                .Append(EscapeQuotedText(definition.Name))
                .Append("\" ");
            AppendAttributeType(builder, definition.ValueKind, definition.Minimum, definition.Maximum, definition.EnumValues);
            builder.Append(';').Append(newline);
        }
    }

    private static void AppendAttributeDefaults(StringBuilder builder, DbcDocument document, string newline)
    {
        foreach (var definition in document.AttributeDefinitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            if (definition.DefaultValue is null)
            {
                continue;
            }

            builder.Append("BA_DEF_DEF_ \"")
                .Append(EscapeQuotedText(definition.Name))
                .Append("\" ")
                .Append(FormatAttributeValue(definition.DefaultValue))
                .Append(';')
                .Append(newline);
        }
    }

    private static void AppendAttributeValues(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var value in document.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            builder.Append("BA_ \"")
                .Append(EscapeQuotedText(value.Name))
                .Append("\" ")
                .Append(FormatAttributeValue(value))
                .Append(';')
                .Append(newline);
        }

        foreach (var node in GetAttributeNodes(document, options))
        {
            foreach (var value in node.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                builder.Append("BA_ \"")
                    .Append(EscapeQuotedText(value.Name))
                    .Append("\" BU_ ")
                    .Append(DbcWriterNameFormatter.GetNodeExportName(node, options))
                    .Append(' ')
                    .Append(FormatAttributeValue(value))
                    .Append(';')
                    .Append(newline);
            }
        }

        foreach (var message in EnumerateMessages(document, options))
        {
            foreach (var value in message.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                builder.Append("BA_ \"")
                    .Append(EscapeQuotedText(value.Name))
                    .Append("\" BO_ ")
                    .Append(message.RawId.Value)
                    .Append(' ')
                    .Append(FormatAttributeValue(value))
                    .Append(';')
                    .Append(newline);
            }

            foreach (var signal in EnumerateSignals(message, options))
            {
                foreach (var value in signal.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    builder.Append("BA_ \"")
                        .Append(EscapeQuotedText(value.Name))
                        .Append("\" SG_ ")
                        .Append(message.RawId.Value)
                        .Append(' ')
                        .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options))
                        .Append(' ')
                        .Append(FormatAttributeValue(value))
                        .Append(';')
                        .Append(newline);
                }
            }
        }

        foreach (var variable in EnumerateEnvironmentVariables(document, options))
        {
            foreach (var value in variable.Attributes.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                builder.Append("BA_ \"")
                    .Append(EscapeQuotedText(value.Name))
                    .Append("\" EV_ ")
                    .Append(DbcWriterNameFormatter.GetEnvironmentVariableExportName(variable, options))
                    .Append(' ')
                    .Append(FormatAttributeValue(value))
                    .Append(';')
                    .Append(newline);
            }
        }
    }

    private static IEnumerable<DbcNode> GetAttributeNodes(DbcDocument document, DbcWriterOptions options)
    {
        var nodesByExportName = new Dictionary<string, DbcNode>(StringComparer.Ordinal);
        foreach (var node in EnumerateReferencedNodes(document))
        {
            var exportName = DbcWriterNameFormatter.GetNodeExportName(node, options);
            if (!nodesByExportName.TryGetValue(exportName, out var existingNode) ||
                existingNode.Attributes.Count == 0 && node.Attributes.Count > 0)
            {
                nodesByExportName[exportName] = node;
            }
        }

        return options.SortMode == DbcWriterSortMode.Stable
            ? nodesByExportName.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value)
            : nodesByExportName.Values;
    }

    private static void AppendRelationAttributes(StringBuilder builder, DbcDocument document, string newline)
    {
        foreach (var definition in document.RelationAttributeDefinitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            builder.Append("BA_DEF_REL_ ")
                .Append(definition.RelationKind)
                .Append(" \"")
                .Append(EscapeQuotedText(definition.Name))
                .Append("\" ");
            AppendAttributeType(builder, definition.ValueKind, definition.Minimum, definition.Maximum, definition.EnumValues);
            builder.Append(';').Append(newline);
        }

        foreach (var item in document.RelationAttributeDefaults.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            builder.Append("BA_DEF_DEF_REL_ \"")
                .Append(EscapeQuotedText(item.Name))
                .Append("\" ")
                .Append(FormatRawMetadataValue(item.RawValue))
                .Append(';')
                .Append(newline);
        }

        foreach (var item in document.RelationAttributes)
        {
            builder.Append("BA_REL_ \"")
                .Append(EscapeQuotedText(item.Name))
                .Append("\" ")
                .Append(item.Target)
                .Append(' ')
                .Append(FormatRawMetadataValue(item.RawValue))
                .Append(';')
                .Append(newline);
        }
    }

    private static void AppendComments(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        if (document.Comment is not null)
        {
            builder.Append("CM_ \"").Append(EscapeQuotedText(document.Comment)).Append("\";").Append(newline);
        }

        foreach (var node in GetCommentNodes(document, options))
        {
            if (node.Comment is null)
            {
                continue;
            }

            builder.Append("CM_ BU_ ")
                .Append(DbcWriterNameFormatter.GetNodeExportName(node, options))
                .Append(" \"")
                .Append(EscapeQuotedText(node.Comment))
                .Append("\";")
                .Append(newline);
        }

        foreach (var message in EnumerateMessages(document, options))
        {
            if (message.Comment is not null)
            {
                builder.Append("CM_ BO_ ")
                    .Append(message.RawId.Value)
                    .Append(" \"")
                    .Append(EscapeQuotedText(message.Comment))
                    .Append("\";")
                    .Append(newline);
            }

            foreach (var signal in EnumerateSignals(message, options))
            {
                if (signal.Comment is null)
                {
                    continue;
                }

                builder.Append("CM_ SG_ ")
                    .Append(message.RawId.Value)
                    .Append(' ')
                    .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options))
                    .Append(" \"")
                    .Append(EscapeQuotedText(signal.Comment))
                    .Append("\";")
                    .Append(newline);
            }
        }
    }

    private static IEnumerable<DbcNode> GetCommentNodes(DbcDocument document, DbcWriterOptions options)
    {
        var nodesByExportName = new Dictionary<string, DbcNode>(StringComparer.Ordinal);
        foreach (var node in EnumerateReferencedNodes(document))
        {
            var exportName = DbcWriterNameFormatter.GetNodeExportName(node, options);
            if (!nodesByExportName.TryGetValue(exportName, out var existingNode) ||
                existingNode.Comment is null && node.Comment is not null)
            {
                nodesByExportName[exportName] = node;
            }
        }

        return options.SortMode == DbcWriterSortMode.Stable
            ? nodesByExportName.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value)
            : nodesByExportName.Values;
    }

    private static IEnumerable<DbcNode> EnumerateReferencedNodes(DbcDocument document)
    {
        foreach (var node in document.Nodes)
        {
            yield return node;
        }

        foreach (var message in document.Messages)
        {
            yield return message.PrimaryTransmitter;

            foreach (var transmitter in message.Transmitters)
            {
                yield return transmitter;
            }

            foreach (var signal in message.Signals)
            {
                foreach (var receiver in signal.Receivers)
                {
                    yield return receiver;
                }
            }
        }

        foreach (var variable in document.EnvironmentVariables.Values)
        {
            foreach (var node in variable.AccessNodes)
            {
                yield return node;
            }
        }
    }

    private static void AppendValueDescriptions(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var message in EnumerateMessages(document, options))
        {
            foreach (var signal in EnumerateSignals(message, options))
            {
                if (signal.ValueDescriptions.Count == 0)
                {
                    continue;
                }

                builder.Append("VAL_ ")
                    .Append(message.RawId.Value)
                    .Append(' ')
                    .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options));

                foreach (var valueDescription in signal.ValueDescriptions.OrderBy(item => item.Key))
                {
                    builder.Append(' ')
                        .Append(valueDescription.Key.ToString(CultureInfo.InvariantCulture))
                        .Append(" \"")
                        .Append(EscapeQuotedText(valueDescription.Value))
                        .Append('"');
                }

                builder.Append(';').Append(newline);
            }
        }
    }

    private static void AppendSignalValueTypes(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var message in EnumerateMessages(document, options))
        {
            foreach (var signal in EnumerateSignals(message, options))
            {
                var typeCode = signal.ValueType switch
                {
                    DbcSignalValueType.Float => 1,
                    DbcSignalValueType.Double => 2,
                    _ => 0,
                };

                if (typeCode == 0)
                {
                    continue;
                }

                builder.Append("SIG_VALTYPE_ ")
                    .Append(message.RawId.Value)
                    .Append(' ')
                    .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options))
                    .Append(" : ")
                    .Append(typeCode)
                    .Append(';')
                    .Append(newline);
            }
        }
    }

    private static void AppendExtendedMultiplexing(StringBuilder builder, DbcDocument document, DbcWriterOptions options, string newline)
    {
        foreach (var message in EnumerateMessages(document, options))
        {
            foreach (var signal in EnumerateSignals(message, options))
            {
                if (signal.Multiplexing.SwitchRanges.Count == 0 ||
                    string.IsNullOrEmpty(signal.Multiplexing.MultiplexorSignalName) ||
                    !DbcWriterNameFormatter.TryResolveMultiplexorSignal(message, signal.Multiplexing.MultiplexorSignalName, options, out var multiplexor))
                {
                    continue;
                }

                builder.Append("SG_MUL_VAL_ ")
                    .Append(message.RawId.Value)
                    .Append(' ')
                    .Append(DbcWriterNameFormatter.GetSignalExportName(signal, options))
                    .Append(' ')
                    .Append(DbcWriterNameFormatter.GetSignalExportName(multiplexor, options))
                    .Append(' ');

                for (var i = 0; i < signal.Multiplexing.SwitchRanges.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    var range = signal.Multiplexing.SwitchRanges[i];
                    builder.Append(range.Minimum.ToString(CultureInfo.InvariantCulture))
                        .Append('-')
                        .Append(range.Maximum.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append(';').Append(newline);
            }
        }
    }

    private static string GetMultiplexingToken(DbcMultiplexing multiplexing)
    {
        return multiplexing.Role switch
        {
            DbcMultiplexingRole.Multiplexor => " M",
            DbcMultiplexingRole.Multiplexed when multiplexing.SwitchValue is { } switchValue => " m" + switchValue.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    private static string FormatReceivers(IReadOnlyList<DbcNode> receivers, DbcWriterOptions options)
    {
        if (receivers.Count == 0)
        {
            return "Vector__XXX";
        }

        return string.Join(",", receivers.Select(receiver => DbcWriterNameFormatter.GetNodeExportName(receiver, options)));
    }

    private static string FormatNumber(double value)
    {
        return value == 0
            ? "0"
            : value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string GetOwnerToken(DbcAttributeOwnerKind ownerKind)
    {
        return ownerKind switch
        {
            DbcAttributeOwnerKind.Node => "BU_",
            DbcAttributeOwnerKind.Message => "BO_",
            DbcAttributeOwnerKind.Signal => "SG_",
            DbcAttributeOwnerKind.EnvironmentVariable => "EV_",
            _ => string.Empty,
        };
    }

    private static void AppendAttributeType(
        StringBuilder builder,
        DbcAttributeValueKind valueKind,
        double? minimum,
        double? maximum,
        IReadOnlyList<string> enumValues)
    {
        switch (valueKind)
        {
            case DbcAttributeValueKind.Integer:
                builder.Append("INT ").Append(FormatNumber(minimum ?? 0)).Append(' ').Append(FormatNumber(maximum ?? 0));
                break;
            case DbcAttributeValueKind.Hex:
                builder.Append("HEX ").Append(FormatNumber(minimum ?? 0)).Append(' ').Append(FormatNumber(maximum ?? 0));
                break;
            case DbcAttributeValueKind.Float:
                builder.Append("FLOAT ").Append(FormatNumber(minimum ?? 0)).Append(' ').Append(FormatNumber(maximum ?? 0));
                break;
            case DbcAttributeValueKind.Enum:
                builder.Append("ENUM ");
                builder.Append(string.Join(",", enumValues.Select(value => "\"" + EscapeQuotedText(value) + "\"")));
                break;
            default:
                builder.Append("STRING");
                break;
        }
    }

    private static string FormatAttributeValue(DbcAttributeValue value)
    {
        return value.ValueKind switch
        {
            DbcAttributeValueKind.String => "\"" + EscapeQuotedText(value.RawValue) + "\"",
            DbcAttributeValueKind.Enum when IsNumericAttributeRawValue(value.RawValue) => value.RawValue,
            DbcAttributeValueKind.Enum => "\"" + EscapeQuotedText(value.RawValue) + "\"",
            _ => value.RawValue,
        };
    }

    private static string FormatRawMetadataValue(string rawValue)
    {
        return IsNumericAttributeRawValue(rawValue) || DbcWriteValidator.IsValidIdentifier(rawValue)
            ? rawValue
            : "\"" + EscapeQuotedText(rawValue) + "\"";
    }

    private static bool IsNumericAttributeRawValue(string rawValue)
    {
        if (rawValue.Length == 0 ||
            !string.Equals(rawValue, rawValue.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return rawValue.Length > 2 &&
                ulong.TryParse(rawValue[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed);
    }

    private static string EscapeQuotedText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
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

    internal static string GetMessageExportName(DbcMessage message, DbcWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        return options.NameExportPolicy == DbcNameExportPolicy.UseCanonicalNamesWhenValid &&
            DbcWriteValidator.IsValidIdentifier(message.Name)
                ? message.Name
                : message.SourceName;
    }

    internal static string GetSignalExportName(DbcSignal signal, DbcWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(options);

        return options.NameExportPolicy == DbcNameExportPolicy.UseCanonicalNamesWhenValid &&
            DbcWriteValidator.IsValidIdentifier(signal.Name)
                ? signal.Name
                : signal.SourceName;
    }

    internal static string GetEnvironmentVariableExportName(DbcEnvironmentVariable variable, DbcWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(options);

        return options.NameExportPolicy == DbcNameExportPolicy.UseCanonicalNamesWhenValid &&
            DbcWriteValidator.IsValidIdentifier(variable.Name)
                ? variable.Name
                : variable.SourceName;
    }

    internal static bool TryResolveMultiplexorSignal(
        DbcMessage message,
        string multiplexorSignalName,
        DbcWriterOptions options,
        out DbcSignal multiplexor)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(multiplexorSignalName);
        ArgumentNullException.ThrowIfNull(options);

        DbcSignal? aliasMatch = null;
        var aliasMatchCount = 0;
        DbcSignal? exportNameMatch = null;
        var exportNameMatchCount = 0;

        foreach (var candidate in message.Signals)
        {
            if (candidate.Multiplexing.Role != DbcMultiplexingRole.Multiplexor)
            {
                continue;
            }

            if (DbcNameLookup.Matches(candidate.Name, candidate.NameAliases, multiplexorSignalName))
            {
                aliasMatch = candidate;
                aliasMatchCount++;
            }

            if (string.Equals(GetSignalExportName(candidate, options), multiplexorSignalName, StringComparison.Ordinal))
            {
                exportNameMatch = candidate;
                exportNameMatchCount++;
            }
        }

        if (aliasMatchCount == 1)
        {
            multiplexor = aliasMatch!;
            return true;
        }

        if (aliasMatchCount == 0 && exportNameMatchCount == 1)
        {
            multiplexor = exportNameMatch!;
            return true;
        }

        multiplexor = null!;
        return false;
    }
}
