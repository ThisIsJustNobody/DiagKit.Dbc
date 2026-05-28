using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

internal static partial class DbcWriteValidator
{
    private const string EmptyReceiverSentinel = "Vector__XXX";
    private const int MaxSignalBitLength = 64;
    private const DbcFrameFlags UnsupportedFrameFlags =
        DbcFrameFlags.BitRateSwitch | DbcFrameFlags.ErrorStateIndicator;
    private static readonly HashSet<string> LongSymbolAttributeNames = new(StringComparer.Ordinal)
    {
        "SystemNodeLongSymbol",
        "SystemMessageLongSymbol",
        "SystemSignalLongSymbol",
        "SystemEnvVarLongSymbol",
    };

    public static DbcValidationResult Validate(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        var metadataValidatedNodes = new HashSet<DbcNode>();
        ValidateDocumentMetadata(document, diagnostics);
        ValidateObjectName("node", document.Nodes.Select(x => DbcWriterNameFormatter.GetNodeExportName(x, options)), diagnostics);
        ValidateObjectName("message", document.Messages.Select(x => DbcWriterNameFormatter.GetMessageExportName(x, options)), diagnostics);
        ValidateObjectName("environment variable", document.EnvironmentVariables.Values.Select(x => DbcWriterNameFormatter.GetEnvironmentVariableExportName(x, options)), diagnostics);
        ValidateReferencedNodeMetadataCollisions(document, options, diagnostics);
        foreach (var node in document.Nodes)
        {
            ValidateLongSymbolExport("Node", node.Name, DbcWriterNameFormatter.GetNodeExportName(node, options), diagnostics);
            ValidateNodeMetadataOnce(node, metadataValidatedNodes, document, diagnostics);
        }

        foreach (var variable in document.EnvironmentVariables.Values)
        {
            ValidateLongSymbolExport("Environment variable", variable.Name, DbcWriterNameFormatter.GetEnvironmentVariableExportName(variable, options), diagnostics);
            ValidateEnvironmentVariable(variable, diagnostics);
            ValidateAttributeValues("Environment variable", variable.Name, variable.Attributes, document, diagnostics);
            foreach (var accessNode in variable.AccessNodes)
            {
                var accessNodeName = DbcWriterNameFormatter.GetNodeExportName(accessNode, options);
                ValidateLongSymbolExport("Node", accessNode.Name, accessNodeName, diagnostics);
                ValidateNodeMetadataOnce(accessNode, metadataValidatedNodes, document, diagnostics);
                if (!IsValidIdentifier(accessNodeName))
                {
                    diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Environment variable '{variable.Name}' access node name '{accessNodeName}' is not a valid DBC identifier."));
                }
            }
        }

        foreach (var message in document.Messages)
        {
            ValidateLongSymbolExport("Message", message.Name, DbcWriterNameFormatter.GetMessageExportName(message, options), diagnostics);
            ValidateMessageMetadata(message, document, diagnostics);
            ValidateTransmitters(message, options, metadataValidatedNodes, document, diagnostics);

            if (!message.SupportsSingleFrameRuntime)
            {
                diagnostics.Add(new DbcDiagnostic(
                    DbcDiagnosticSeverity.Warning,
                    "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE",
                    $"Message '{message.Name}' payload length {message.DataLength} can be exported as metadata but is not supported by the CAN/CAN FD single-frame runtime."));
            }

            var transmitterName = DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options);
            ValidateLongSymbolExport("Node", message.PrimaryTransmitter.Name, transmitterName, diagnostics);
            ValidateNodeMetadataOnce(message.PrimaryTransmitter, metadataValidatedNodes, document, diagnostics);
            if (!IsValidIdentifier(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Message '{message.Name}' transmitter name '{transmitterName}' is not a valid DBC identifier."));
            }

            ValidateObjectName($"signal in message '{message.Name}'", message.Signals.Select(x => DbcWriterNameFormatter.GetSignalExportName(x, options)), diagnostics);
            foreach (var signal in message.Signals)
            {
                ValidateLongSymbolExport("Signal", signal.Name, DbcWriterNameFormatter.GetSignalExportName(signal, options), diagnostics);
                ValidateSignalMetadata(message, signal, document, diagnostics);
                ValidateSignal(message, signal, options, diagnostics);

                foreach (var receiver in signal.Receivers)
                {
                    var receiverName = DbcWriterNameFormatter.GetNodeExportName(receiver, options);
                    ValidateLongSymbolExport("Node", receiver.Name, receiverName, diagnostics);
                    ValidateNodeMetadataOnce(receiver, metadataValidatedNodes, document, diagnostics);
                    if (!IsValidIdentifier(receiverName))
                    {
                        diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Signal '{message.Name}.{signal.Name}' receiver name '{receiverName}' is not a valid DBC identifier."));
                    }

                    if (string.Equals(receiverName, EmptyReceiverSentinel, StringComparison.Ordinal))
                    {
                        diagnostics.Add(Error(
                            "DBC_WRITE_RESERVED_RECEIVER_NAME",
                            $"Signal '{message.Name}.{signal.Name}' receiver name '{receiverName}' is reserved for empty receiver lists in normalized DBC export."));
                    }
                }
            }
        }

        return new DbcValidationResult(diagnostics);
    }

    private static void ValidateReferencedNodeMetadataCollisions(
        DbcDocument document,
        DbcWriterOptions options,
        List<DbcDiagnostic> diagnostics)
    {
        var commentsByExportName = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributesByExportName = new Dictionary<string, IReadOnlyDictionary<string, DbcAttributeValue>>(StringComparer.Ordinal);
        foreach (var node in EnumerateReferencedNodes(document))
        {
            var exportName = DbcWriterNameFormatter.GetNodeExportName(node, options);
            if (node.Comment is not null)
            {
                if (commentsByExportName.TryGetValue(exportName, out var existingComment) &&
                    !string.Equals(existingComment, node.Comment, StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(
                        "DBC_WRITE_NAME_COLLISION",
                        $"Node export name '{exportName}' is referenced by multiple nodes with different comments."));
                    continue;
                }

                commentsByExportName[exportName] = node.Comment;
            }

            if (node.Attributes.Count == 0)
            {
                continue;
            }

            if (attributesByExportName.TryGetValue(exportName, out var existingAttributes) &&
                !AttributeDictionariesEqual(existingAttributes, node.Attributes))
            {
                diagnostics.Add(Error(
                    "DBC_WRITE_NAME_COLLISION",
                    $"Node export name '{exportName}' is referenced by multiple nodes with different attributes."));
                continue;
            }

            attributesByExportName[exportName] = node.Attributes;
        }
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

    private static void ValidateTransmitters(
        DbcMessage message,
        DbcWriterOptions options,
        HashSet<DbcNode> metadataValidatedNodes,
        DbcDocument document,
        List<DbcDiagnostic> diagnostics)
    {
        var primaryName = DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options);
        var hasPrimary = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transmitter in message.Transmitters)
        {
            var transmitterName = DbcWriterNameFormatter.GetNodeExportName(transmitter, options);
            ValidateLongSymbolExport("Node", transmitter.Name, transmitterName, diagnostics);
            ValidateNodeMetadataOnce(transmitter, metadataValidatedNodes, document, diagnostics);
            if (!IsValidIdentifier(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Message '{message.Name}' transmitter name '{transmitterName}' is not a valid DBC identifier."));
            }

            if (!seen.Add(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_NAME_COLLISION", $"Message '{message.Name}' transmitter export name '{transmitterName}' appears more than once."));
            }

            hasPrimary |= string.Equals(transmitterName, primaryName, StringComparison.Ordinal);
        }

        if (!hasPrimary)
        {
            diagnostics.Add(Error(
                "DBC_WRITE_UNSUPPORTED_ADDITIONAL_TRANSMITTERS",
                $"Message '{message.Name}' transmitter list does not include the primary transmitter '{primaryName}', which would not reload equivalently."));
        }
    }

    private static void ValidateDocumentMetadata(DbcDocument document, List<DbcDiagnostic> diagnostics)
    {
        ValidateOptionalQuotedText("Document", "network", "comment", document.Comment, diagnostics);
        foreach (var definition in document.AttributeDefinitions.Values)
        {
            ValidateLongSymbolAttributeName("Attribute definition", definition.Name, diagnostics);
            ValidateAttributeDefinition(
                "Attribute definition",
                definition.Name,
                definition.ValueKind,
                definition.Minimum,
                definition.Maximum,
                definition.EnumValues,
                diagnostics);
            if (definition.DefaultValue is not null)
            {
                ValidateAttributeValue("Attribute default", "network", definition.DefaultValue, definition, diagnostics);
            }
        }

        ValidateAttributeValues("Document", "network", document.Attributes, document, diagnostics);
        ValidateRelationMetadata(document, diagnostics);
    }

    private static void ValidateRelationMetadata(DbcDocument document, List<DbcDiagnostic> diagnostics)
    {
        foreach (var definition in document.RelationAttributeDefinitions.Values)
        {
            ValidateQuotedText("Relation attribute definition", definition.Name, "name", definition.Name, diagnostics);
            if (!IsValidIdentifier(definition.RelationKind))
            {
                diagnostics.Add(Error(
                    "DBC_WRITE_INVALID_RELATION_METADATA",
                    $"Relation attribute definition '{definition.Name}' relation kind '{definition.RelationKind}' is not a valid DBC relation token."));
            }

            ValidateAttributeDefinition(
                "Relation attribute definition",
                definition.Name,
                definition.ValueKind,
                definition.Minimum,
                definition.Maximum,
                definition.EnumValues,
                diagnostics);
        }

        foreach (var item in document.RelationAttributeDefaults.Values)
        {
            ValidateQuotedText("Relation attribute default", item.Name, "name", item.Name, diagnostics);
            if (!document.RelationAttributeDefinitions.TryGetValue(item.Name, out var definition))
            {
                AddUnsupportedMetadata($"Relation attribute default '{item.Name}' has no BA_DEF_REL_ definition and would not reload equivalently.", diagnostics);
                continue;
            }

            ValidateRelationAttributeRawValue("Relation attribute default", "network", item.Name, item.RawValue, definition, diagnostics);
        }

        foreach (var item in document.RelationAttributes)
        {
            ValidateQuotedText("Relation attribute", item.Name, "name", item.Name, diagnostics);
            if (!IsSafeRelationTarget(item.Target))
            {
                diagnostics.Add(Error(
                    "DBC_WRITE_INVALID_RELATION_METADATA",
                    $"Relation attribute '{item.Name}' target '{item.Target}' contains text that cannot be emitted as reloadable DBC relation metadata."));
            }

            if (!document.RelationAttributeDefinitions.TryGetValue(item.Name, out var definition))
            {
                AddUnsupportedMetadata($"Relation attribute '{item.Name}' has no BA_DEF_REL_ definition and would not reload equivalently.", diagnostics);
                continue;
            }

            ValidateRelationAttributeRawValue("Relation attribute", item.Target, item.Name, item.RawValue, definition, diagnostics);
        }
    }

    private static void ValidateAttributeDefinition(
        string definitionKind,
        string name,
        DbcAttributeValueKind valueKind,
        double? minimum,
        double? maximum,
        IReadOnlyList<string> enumValues,
        List<DbcDiagnostic> diagnostics)
    {
        ValidateQuotedText(definitionKind, name, "name", name, diagnostics);
        if (valueKind == DbcAttributeValueKind.Enum)
        {
            foreach (var enumValue in enumValues)
            {
                if (!IsSafeQuotedTextValue(enumValue))
                {
                    AddInvalidAttributeDefinition(
                        definitionKind,
                        name,
                        "enum labels must not contain control characters.",
                        diagnostics);
                    break;
                }
            }
        }

        if (valueKind is DbcAttributeValueKind.String or DbcAttributeValueKind.Enum)
        {
            if (minimum.HasValue || maximum.HasValue)
            {
                AddInvalidAttributeDefinition(
                    definitionKind,
                    name,
                    "non-numeric attribute definitions must not carry numeric range metadata.",
                    diagnostics);
            }

            return;
        }

        if (!minimum.HasValue || !maximum.HasValue)
        {
            AddInvalidAttributeDefinition(
                definitionKind,
                name,
                "numeric attribute definitions must include both minimum and maximum values.",
                diagnostics);
            return;
        }

        if (!double.IsFinite(minimum.Value) ||
            !double.IsFinite(maximum.Value) ||
            maximum.Value < minimum.Value)
        {
            AddInvalidAttributeDefinition(
                definitionKind,
                name,
                "numeric attribute definitions must use a finite range where maximum is greater than or equal to minimum.",
                diagnostics);
            return;
        }

        if ((valueKind is DbcAttributeValueKind.Integer or DbcAttributeValueKind.Hex) &&
            (!IsWholeNumber(minimum.Value) || !IsWholeNumber(maximum.Value)))
        {
            AddInvalidAttributeDefinition(
                definitionKind,
                name,
                "integer and hex attribute definition ranges must be whole numbers.",
                diagnostics);
            return;
        }

        if (valueKind == DbcAttributeValueKind.Hex && minimum.Value < 0)
        {
            AddInvalidAttributeDefinition(
                definitionKind,
                name,
                "hex attribute definition ranges must be non-negative.",
                diagnostics);
        }
    }

    private static void ValidateRelationAttributeRawValue(
        string objectKind,
        string objectName,
        string attributeName,
        string rawValue,
        DbcRelationAttributeDefinition definition,
        List<DbcDiagnostic> diagnostics)
    {
        var value = new DbcAttributeValue(attributeName, definition.ValueKind, rawValue, rawValue);
        var attributeDefinition = new DbcAttributeDefinition(
            definition.Name,
            DbcAttributeOwnerKind.Network,
            definition.ValueKind,
            definition.EnumValues,
            definition.Minimum,
            definition.Maximum);
        ValidateAttributeValue(objectKind, objectName, value, attributeDefinition, diagnostics);
    }

    private static bool IsWholeNumber(double value)
    {
        return value == Math.Truncate(value);
    }

    private static bool IsSafeRelationTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) ||
            !string.Equals(target.Trim(), target, StringComparison.Ordinal))
        {
            return false;
        }

        var previousWasSpace = false;
        foreach (var character in target)
        {
            if (character is ';' or '"' or '\\' ||
                char.IsControl(character) ||
                char.IsWhiteSpace(character) && character != ' ')
            {
                return false;
            }

            if (character == ' ')
            {
                if (previousWasSpace)
                {
                    return false;
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
        }

        return true;
    }

    private static void ValidateNodeMetadataOnce(
        DbcNode node,
        HashSet<DbcNode> validatedNodes,
        DbcDocument document,
        List<DbcDiagnostic> diagnostics)
    {
        if (!validatedNodes.Add(node))
        {
            return;
        }

        if (node.Attributes.Count > 0)
        {
            ValidateAttributeValues("Node", node.Name, node.Attributes, document, diagnostics);
        }

        ValidateOptionalQuotedText("Node", node.Name, "comment", node.Comment, diagnostics);
    }

    private static void ValidateMessageMetadata(DbcMessage message, DbcDocument document, List<DbcDiagnostic> diagnostics)
    {
        ValidateAttributeValues("Message", message.Name, message.Attributes, document, diagnostics);
        ValidateOptionalQuotedText("Message", message.Name, "comment", message.Comment, diagnostics);

        if (message.CycleTimeMs.HasValue &&
            (!TryGetAttributeInt32(message.Attributes, "GenMsgCycleTime", out var cycleTimeMs) || cycleTimeMs != message.CycleTimeMs.Value))
        {
            AddUnsupportedMetadata($"Message '{message.Name}' cycle time metadata is not backed by a matching GenMsgCycleTime attribute.", diagnostics);
        }

        if (message.SendType != DbcSendType.Unknown &&
            (!TryGetAttributeSendType(message.Attributes, "GenMsgSendType", out var sendType) || sendType != message.SendType))
        {
            AddUnsupportedMetadata($"Message '{message.Name}' send type metadata is not backed by a matching GenMsgSendType attribute.", diagnostics);
        }

        if (message.TimeoutTimeMs.HasValue &&
            (!TryGetAttributeInt32(message.Attributes, "GenMsgTimeoutTime", out var timeoutTimeMs) || timeoutTimeMs != message.TimeoutTimeMs.Value))
        {
            AddUnsupportedMetadata($"Message '{message.Name}' timeout metadata is not backed by a matching GenMsgTimeoutTime attribute.", diagnostics);
        }

        ValidateFlexibleDataRateMetadata(message, diagnostics);

        var unsupportedFrameFlags = message.FrameFlags & UnsupportedFrameFlags;
        if (unsupportedFrameFlags != DbcFrameFlags.None)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' frame flags '{unsupportedFrameFlags}' are not supported by the current normalized export.", diagnostics);
        }
    }

    private static void ValidateFlexibleDataRateMetadata(DbcMessage message, List<DbcDiagnostic> diagnostics)
    {
        var frameFlagsHaveFlexibleDataRate = (message.FrameFlags & DbcFrameFlags.FlexibleDataRate) != 0;
        var lengthAutomaticallyRestoresFlexibleDataRate = message.DataLength is > 8 and <= 64;
        var attributeHasFlexibleDataRate = message.Attributes.TryGetValue("VFrameFormat", out var frameFormatAttribute) &&
            IsCanFdFrameFormat(frameFormatAttribute);

        if (frameFlagsHaveFlexibleDataRate && !lengthAutomaticallyRestoresFlexibleDataRate && !attributeHasFlexibleDataRate ||
            !frameFlagsHaveFlexibleDataRate && attributeHasFlexibleDataRate)
        {
            AddUnsupportedMetadata(
                $"Message '{message.Name}' FlexibleDataRate flag is not consistent with its VFrameFormat attribute and would not reload equivalently.",
                diagnostics);
        }
    }

    private static void ValidateSignalMetadata(DbcMessage message, DbcSignal signal, DbcDocument document, List<DbcDiagnostic> diagnostics)
    {
        ValidateAttributeValues("Signal", $"{message.Name}.{signal.Name}", signal.Attributes, document, diagnostics);
        ValidateOptionalQuotedText("Signal", $"{message.Name}.{signal.Name}", "comment", signal.Comment, diagnostics);
        foreach (var valueDescription in signal.ValueDescriptions)
        {
            ValidateQuotedText(
                "Signal value description",
                $"{message.Name}.{signal.Name}",
                valueDescription.Key.ToString(CultureInfo.InvariantCulture),
                valueDescription.Value,
                diagnostics);
        }

        if (signal.InitialValue.HasValue &&
            (!TryGetAttributeDouble(signal.Attributes, "GenSigStartValue", out var initialValue) || initialValue != signal.InitialValue.Value))
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' initial value metadata is not backed by a matching GenSigStartValue attribute.", diagnostics);
        }

        if (signal.SendType != DbcSendType.Unknown &&
            (!TryGetAttributeSendType(signal.Attributes, "GenSigSendType", out var sendType) || sendType != signal.SendType))
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' send type metadata is not backed by a matching GenSigSendType attribute.", diagnostics);
        }

        if (signal.TimeoutTimeMs.HasValue &&
            (!TryGetAttributeInt32(signal.Attributes, "GenSigTimeoutTime", out var timeoutTimeMs) || timeoutTimeMs != signal.TimeoutTimeMs.Value))
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' timeout metadata is not backed by a matching GenSigTimeoutTime attribute.", diagnostics);
        }
    }

    private static void ValidateSignal(DbcMessage message, DbcSignal signal, DbcWriterOptions options, List<DbcDiagnostic> diagnostics)
    {
        ValidateSignalBitRange(message, signal, diagnostics);

        if (HasUnsupportedMultiplexing(message, signal, options))
        {
            diagnostics.Add(Error(
                "DBC_WRITE_UNSUPPORTED_MULTIPLEXING",
                $"Signal '{message.Name}.{signal.Name}' uses unsupported multiplexing for the current normalized export."));
        }

        ValidateFiniteSignalNumber(message, signal, nameof(signal.Factor), signal.Factor, diagnostics);
        ValidateFiniteSignalNumber(message, signal, nameof(signal.Offset), signal.Offset, diagnostics);
        ValidateFiniteSignalNumber(message, signal, nameof(signal.Minimum), signal.Minimum, diagnostics);
        ValidateFiniteSignalNumber(message, signal, nameof(signal.Maximum), signal.Maximum, diagnostics);
        ValidateQuotedText("Signal", $"{message.Name}.{signal.Name}", nameof(signal.Unit), signal.Unit, diagnostics);
    }

    private static void ValidateLongSymbolExport(string objectKind, string canonicalName, string exportName, List<DbcDiagnostic> diagnostics)
    {
        if (string.Equals(exportName, canonicalName, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL",
            $"{objectKind} '{canonicalName}' would be exported as '{exportName}', but Task 4 normalized export does not emit Vector long-symbol attributes yet."));
    }

    private static void ValidateAttributeValues(
        string objectKind,
        string objectName,
        IReadOnlyDictionary<string, DbcAttributeValue> attributes,
        DbcDocument document,
        List<DbcDiagnostic> diagnostics)
    {
        foreach (var attribute in attributes.Values)
        {
            ValidateQuotedText($"{objectKind} '{objectName}' attribute", attribute.Name, "name", attribute.Name, diagnostics);
            if (ValidateLongSymbolAttributeName($"{objectKind} '{objectName}' attribute", attribute.Name, diagnostics))
            {
                continue;
            }

            if (document.AttributeDefinitions.ContainsKey(attribute.Name))
            {
                ValidateAttributeValue(objectKind, objectName, attribute, document.AttributeDefinitions[attribute.Name], diagnostics);
                continue;
            }

            AddUnsupportedMetadata(
                $"{objectKind} '{objectName}' attribute '{attribute.Name}' has no BA_DEF_ definition and would not reload equivalently.",
                diagnostics);
        }
    }

    private static void ValidateAttributeValue(
        string objectKind,
        string objectName,
        DbcAttributeValue value,
        DbcAttributeDefinition definition,
        List<DbcDiagnostic> diagnostics)
    {
        if (value.ValueKind != definition.ValueKind)
        {
            diagnostics.Add(Error(
                "DBC_WRITE_INVALID_ATTRIBUTE_VALUE",
                $"{objectKind} '{objectName}' attribute '{value.Name}' kind '{value.ValueKind}' does not match its definition kind '{definition.ValueKind}'."));
            return;
        }

        switch (definition.ValueKind)
        {
            case DbcAttributeValueKind.Integer:
                if (!long.TryParse(value.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }

                break;
            case DbcAttributeValueKind.Hex:
                if (!TryParseHexOrDecimalInteger(value.RawValue))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }

                break;
            case DbcAttributeValueKind.Float:
                if (!double.TryParse(value.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                    !double.IsFinite(parsed))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }

                break;
            case DbcAttributeValueKind.String:
                if (!IsSafeQuotedTextValue(value.RawValue))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }

                break;
            case DbcAttributeValueKind.Enum:
                if (!IsValidEnumAttributeValue(value.RawValue, definition.EnumValues))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }
                else if (!IsNumericAttributeRawValue(value.RawValue) &&
                    !IsSafeQuotedTextValue(value.RawValue))
                {
                    AddInvalidAttributeValue(objectKind, objectName, value.Name, value.RawValue, diagnostics);
                }

                break;
        }
    }

    private static bool TryParseHexOrDecimalInteger(string rawValue)
    {
        if (rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(rawValue[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        return ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsValidEnumAttributeValue(string rawValue, IReadOnlyList<string> enumValues)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return index >= 0 && index < enumValues.Count;
        }

        return enumValues.Contains(rawValue, StringComparer.Ordinal);
    }

    private static bool IsNumericAttributeRawValue(string rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static void AddInvalidAttributeValue(
        string objectKind,
        string objectName,
        string attributeName,
        string rawValue,
        List<DbcDiagnostic> diagnostics)
    {
        diagnostics.Add(Error(
            "DBC_WRITE_INVALID_ATTRIBUTE_VALUE",
            $"{objectKind} '{objectName}' attribute '{attributeName}' raw value '{rawValue}' is not valid for normalized DBC export."));
    }

    private static void AddInvalidAttributeDefinition(
        string definitionKind,
        string name,
        string reason,
        List<DbcDiagnostic> diagnostics)
    {
        diagnostics.Add(Error(
            "DBC_WRITE_INVALID_ATTRIBUTE_DEFINITION",
            $"{definitionKind} '{name}' is not valid for normalized DBC export: {reason}"));
    }

    private static void ValidateEnvironmentVariable(DbcEnvironmentVariable variable, List<DbcDiagnostic> diagnostics)
    {
        ValidateQuotedText("Environment variable", variable.Name, nameof(variable.Unit), variable.Unit, diagnostics);
        if (!double.IsFinite(variable.Minimum) ||
            !double.IsFinite(variable.Maximum) ||
            !double.IsFinite(variable.InitialValue) ||
            !IsValidIdentifier(variable.AccessType))
        {
            diagnostics.Add(Error(
                "DBC_WRITE_INVALID_ENVIRONMENT_VARIABLE",
                $"Environment variable '{variable.Name}' contains EV_ values that cannot be emitted as reloadable DBC text."));
        }
    }

    private static bool ValidateLongSymbolAttributeName(string ownerDescription, string attributeName, List<DbcDiagnostic> diagnostics)
    {
        if (!LongSymbolAttributeNames.Contains(attributeName))
        {
            return false;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL",
            $"{ownerDescription} '{attributeName}' is reserved for Vector long-symbol export, which remains out of scope for Task 4."));
        return true;
    }

    private static void ValidateOptionalQuotedText(
        string objectKind,
        string objectName,
        string fieldName,
        string? value,
        List<DbcDiagnostic> diagnostics)
    {
        if (value is not null)
        {
            ValidateQuotedText(objectKind, objectName, fieldName, value, diagnostics);
        }
    }

    private static void ValidateQuotedText(
        string objectKind,
        string objectName,
        string fieldName,
        string value,
        List<DbcDiagnostic> diagnostics)
    {
        if (IsSafeQuotedTextValue(value))
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_INVALID_QUOTED_TEXT",
            $"{objectKind} '{objectName}' {fieldName} contains control characters that cannot be emitted as reloadable DBC quoted text."));
    }

    private static bool IsSafeQuotedTextValue(string text)
    {
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AttributeDictionariesEqual(
        IReadOnlyDictionary<string, DbcAttributeValue> left,
        IReadOnlyDictionary<string, DbcAttributeValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, leftValue) in left)
        {
            if (!right.TryGetValue(name, out var rightValue) ||
                leftValue.ValueKind != rightValue.ValueKind ||
                !string.Equals(leftValue.RawValue, rightValue.RawValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetAttributeInt32(
        IReadOnlyDictionary<string, DbcAttributeValue> attributes,
        string name,
        out int value)
    {
        if (attributes.TryGetValue(name, out var attribute) &&
            attribute.TryGetInt32(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetAttributeDouble(
        IReadOnlyDictionary<string, DbcAttributeValue> attributes,
        string name,
        out double value)
    {
        if (attributes.TryGetValue(name, out var attribute) &&
            attribute.TryGetDouble(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetAttributeSendType(
        IReadOnlyDictionary<string, DbcAttributeValue> attributes,
        string name,
        out DbcSendType sendType)
    {
        if (attributes.TryGetValue(name, out var attribute))
        {
            var text = attribute.Value as string ?? attribute.RawValue;
            return TryParseSendType(text, out sendType);
        }

        sendType = DbcSendType.Unknown;
        return false;
    }

    private static bool HasCanFdFrameFormatAttribute(DbcMessage message)
    {
        return message.Attributes.TryGetValue("VFrameFormat", out var value) &&
            IsCanFdFrameFormat(value);
    }

    private static bool IsCanFdFrameFormat(DbcAttributeValue value)
    {
        if (value.Value is string text &&
            (string.Equals(text, "StandardCAN_FD", StringComparison.Ordinal) ||
             string.Equals(text, "ExtendedCAN_FD", StringComparison.Ordinal)))
        {
            return true;
        }

        return value.TryGetInt64(out var rawFrameFormat) &&
            rawFrameFormat is 14 or 15;
    }

    private static bool TryParseSendType(string value, out DbcSendType sendType)
    {
        switch (NormalizeSendTypeToken(value))
        {
            case "none":
            case "notused":
            case "nomsgsendtype":
            case "nosigsendtype":
            case "nosendtype":
                sendType = DbcSendType.None;
                return true;
            case "cyclic":
                sendType = DbcSendType.Cyclic;
                return true;
            case "event":
            case "triggered":
            case "spontan":
            case "spontaneous":
                sendType = DbcSendType.Event;
                return true;
            case "cyclicifactive":
                sendType = DbcSendType.CyclicIfActive;
                return true;
            case "cyclicandevent":
            case "cyclicandtriggered":
                sendType = DbcSendType.CyclicAndEvent;
                return true;
            case "ifactive":
                sendType = DbcSendType.IfActive;
                return true;
            case "onwrite":
                sendType = DbcSendType.OnWrite;
                return true;
            case "onwritewithrepetition":
                sendType = DbcSendType.OnWriteWithRepetition;
                return true;
            case "onchange":
                sendType = DbcSendType.OnChange;
                return true;
            case "onchangewithrepetition":
                sendType = DbcSendType.OnChangeWithRepetition;
                return true;
            case "ifactivewithrepetition":
                sendType = DbcSendType.IfActiveWithRepetition;
                return true;
            default:
                sendType = DbcSendType.Unknown;
                return false;
        }
    }

    private static string NormalizeSendTypeToken(string value)
    {
        var normalized = new char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized[count++] = char.ToLowerInvariant(character);
            }
        }

        return new string(normalized, 0, count);
    }

    private static void ValidateSignalBitRange(DbcMessage message, DbcSignal signal, List<DbcDiagnostic> diagnostics)
    {
        if (signal.StartBit >= 0 &&
            signal.BitLength is >= 1 and <= MaxSignalBitLength &&
            IsSignalRangeWithinPayload(message.DataLength, signal.StartBit, signal.BitLength, signal.ByteOrder))
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_INVALID_SIGNAL_BIT_RANGE",
            $"Signal '{message.Name}.{signal.Name}' bit range {signal.StartBit}|{signal.BitLength} is outside message payload length {message.DataLength} or exceeds the current 64-bit signal limit."));
    }

    private static bool IsSignalRangeWithinPayload(int dataLength, int startBit, int bitLength, DbcByteOrder byteOrder)
    {
        return byteOrder switch
        {
            DbcByteOrder.Intel => (long)startBit + bitLength <= (long)dataLength * 8,
            DbcByteOrder.Motorola => IsMotorolaRangeWithinPayload(dataLength, startBit, bitLength),
            _ => false,
        };
    }

    private static bool IsMotorolaRangeWithinPayload(int dataLength, int startBit, int bitLength)
    {
        var byteIndex = startBit / 8;
        var bitInByte = startBit % 8;
        for (var i = 0; i < bitLength; i++)
        {
            if ((uint)byteIndex >= (uint)dataLength)
            {
                return false;
            }

            bitInByte--;
            if (bitInByte >= 0)
            {
                continue;
            }

            byteIndex++;
            bitInByte = 7;
        }

        return true;
    }

    private static bool HasUnsupportedMultiplexing(DbcMessage message, DbcSignal signal, DbcWriterOptions options)
    {
        var multiplexing = signal.Multiplexing;
        var hasExtendedFields = multiplexing.SwitchRanges.Count > 0 ||
            !string.IsNullOrEmpty(multiplexing.MultiplexorSignalName);

        switch (multiplexing.Role)
        {
            case DbcMultiplexingRole.None:
            case DbcMultiplexingRole.Multiplexor:
                return multiplexing.SwitchValue is not null || hasExtendedFields;
            case DbcMultiplexingRole.Multiplexed:
                return HasUnsupportedMultiplexedState(message, signal, options);
            default:
                return true;
        }
    }

    private static bool HasUnsupportedMultiplexedState(DbcMessage message, DbcSignal signal, DbcWriterOptions options)
    {
        var multiplexing = signal.Multiplexing;
        if (multiplexing.SwitchValue is < 0)
        {
            return true;
        }

        if (multiplexing.SwitchValue is null && multiplexing.SwitchRanges.Count == 0)
        {
            return true;
        }

        if (multiplexing.SwitchRanges.Count == 0)
        {
            return !string.IsNullOrEmpty(multiplexing.MultiplexorSignalName);
        }

        if (string.IsNullOrEmpty(multiplexing.MultiplexorSignalName) ||
            !DbcWriterNameFormatter.TryResolveMultiplexorSignal(message, multiplexing.MultiplexorSignalName, options, out var multiplexor))
        {
            return true;
        }

        if (HasInvalidMultiplexingRange(multiplexing.SwitchRanges))
        {
            return true;
        }

        return ReferenceEquals(multiplexor, signal) ||
            multiplexor.Multiplexing.Role != DbcMultiplexingRole.Multiplexor;
    }

    private static bool HasInvalidMultiplexingRange(IReadOnlyList<DbcMultiplexorRange> ranges)
    {
        foreach (var range in ranges)
        {
            if (range.Minimum < 0 || range.Maximum < range.Minimum)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFiniteSignalNumber(
        DbcMessage message,
        DbcSignal signal,
        string fieldName,
        double value,
        List<DbcDiagnostic> diagnostics)
    {
        if (double.IsFinite(value))
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_NON_FINITE_SIGNAL_NUMBER",
            $"Signal '{message.Name}.{signal.Name}' {fieldName} must be finite for normalized DBC export."));
    }

    internal static bool IsValidIdentifier(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && DbcIdentifierRegex().IsMatch(value);
    }

    private static void ValidateObjectName(string scope, IEnumerable<string> names, List<DbcDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!IsValidIdentifier(name))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"{scope} name '{name}' is not a valid DBC identifier."));
            }
            else if (!seen.Add(name))
            {
                diagnostics.Add(Error("DBC_WRITE_NAME_COLLISION", $"{scope} name '{name}' appears more than once."));
            }
        }
    }

    private static DbcDiagnostic Error(string code, string message)
    {
        return new DbcDiagnostic(DbcDiagnosticSeverity.Error, code, message);
    }

    private static void AddUnsupportedMetadata(string message, List<DbcDiagnostic> diagnostics)
    {
        diagnostics.Add(Error("DBC_WRITE_UNSUPPORTED_METADATA", message));
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DbcIdentifierRegex();
}
