using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

internal static partial class DbcWriteValidator
{
    private const string EmptyReceiverSentinel = "Vector__XXX";
    private const int MaxSignalBitLength = 64;
    private const DbcFrameFlags UnsupportedFrameFlags =
        DbcFrameFlags.BitRateSwitch | DbcFrameFlags.ErrorStateIndicator;

    public static DbcValidationResult Validate(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        var metadataValidatedNodes = new HashSet<DbcNode>();
        ValidateDocumentMetadata(document, diagnostics);
        ValidateObjectName("node", document.Nodes.Select(x => DbcWriterNameFormatter.GetNodeExportName(x, options)), diagnostics);
        ValidateObjectName("message", document.Messages.Select(x => DbcWriterNameFormatter.GetMessageExportName(x, options)), diagnostics);
        ValidateReferencedNodeCommentCollisions(document, options, diagnostics);
        foreach (var node in document.Nodes)
        {
            ValidateLongSymbolExport("Node", node.Name, DbcWriterNameFormatter.GetNodeExportName(node, options), diagnostics);
            ValidateNodeMetadataOnce(node, metadataValidatedNodes, diagnostics);
        }

        foreach (var message in document.Messages)
        {
            ValidateLongSymbolExport("Message", message.Name, DbcWriterNameFormatter.GetMessageExportName(message, options), diagnostics);
            ValidateMessageMetadata(message, diagnostics);

            if (HasUnsupportedTransmitters(message))
            {
                diagnostics.Add(Error(
                    "DBC_WRITE_UNSUPPORTED_ADDITIONAL_TRANSMITTERS",
                    $"Message '{message.Name}' has additional transmitters, but Task 2 normalized export does not emit BO_TX_BU_ yet."));
            }

            if (!message.SupportsSingleFrameRuntime)
            {
                diagnostics.Add(new DbcDiagnostic(
                    DbcDiagnosticSeverity.Warning,
                    "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE",
                    $"Message '{message.Name}' payload length {message.DataLength} can be exported as metadata but is not supported by the CAN/CAN FD single-frame runtime."));
            }

            var transmitterName = DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options);
            ValidateLongSymbolExport("Node", message.PrimaryTransmitter.Name, transmitterName, diagnostics);
            ValidateNodeMetadataOnce(message.PrimaryTransmitter, metadataValidatedNodes, diagnostics);
            if (!IsValidIdentifier(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Message '{message.Name}' transmitter name '{transmitterName}' is not a valid DBC identifier."));
            }

            ValidateObjectName($"signal in message '{message.Name}'", message.Signals.Select(x => DbcWriterNameFormatter.GetSignalExportName(x, options)), diagnostics);
            foreach (var signal in message.Signals)
            {
                ValidateLongSymbolExport("Signal", signal.Name, DbcWriterNameFormatter.GetSignalExportName(signal, options), diagnostics);
                ValidateSignalMetadata(message, signal, diagnostics);
                ValidateSignal(message, signal, options, diagnostics);

                foreach (var receiver in signal.Receivers)
                {
                    var receiverName = DbcWriterNameFormatter.GetNodeExportName(receiver, options);
                    ValidateLongSymbolExport("Node", receiver.Name, receiverName, diagnostics);
                    ValidateNodeMetadataOnce(receiver, metadataValidatedNodes, diagnostics);
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

    private static void ValidateReferencedNodeCommentCollisions(
        DbcDocument document,
        DbcWriterOptions options,
        List<DbcDiagnostic> diagnostics)
    {
        var commentsByExportName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in EnumerateReferencedNodes(document))
        {
            if (node.Comment is null)
            {
                continue;
            }

            var exportName = DbcWriterNameFormatter.GetNodeExportName(node, options);
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

            foreach (var signal in message.Signals)
            {
                foreach (var receiver in signal.Receivers)
                {
                    yield return receiver;
                }
            }
        }
    }

    private static bool HasUnsupportedTransmitters(DbcMessage message)
    {
        if (message.Transmitters.Count != 1)
        {
            return true;
        }

        return !ReferenceEquals(message.Transmitters[0], message.PrimaryTransmitter);
    }

    private static void ValidateDocumentMetadata(DbcDocument document, List<DbcDiagnostic> diagnostics)
    {
        if (document.AttributeDefinitions.Count > 0)
        {
            AddUnsupportedMetadata("Document attribute definitions are not supported by the current normalized export.", diagnostics);
        }

        if (document.Attributes.Count > 0)
        {
            AddUnsupportedMetadata("Document attribute values are not supported by the current normalized export.", diagnostics);
        }

        if (document.EnvironmentVariables.Count > 0)
        {
            AddUnsupportedMetadata("Document environment variables are not supported by the current normalized export.", diagnostics);
        }

        if (document.RelationAttributeDefinitions.Count > 0)
        {
            AddUnsupportedMetadata("Document relation attribute definitions are not supported by the current normalized export.", diagnostics);
        }

        if (document.RelationAttributeDefaults.Count > 0)
        {
            AddUnsupportedMetadata("Document relation attribute defaults are not supported by the current normalized export.", diagnostics);
        }

        if (document.RelationAttributes.Count > 0)
        {
            AddUnsupportedMetadata("Document relation attributes are not supported by the current normalized export.", diagnostics);
        }
    }

    private static void ValidateNodeMetadataOnce(
        DbcNode node,
        HashSet<DbcNode> validatedNodes,
        List<DbcDiagnostic> diagnostics)
    {
        if (!validatedNodes.Add(node))
        {
            return;
        }

        if (node.Attributes.Count > 0)
        {
            AddUnsupportedMetadata($"Node '{node.Name}' attribute values are not supported by the current normalized export.", diagnostics);
        }
    }

    private static void ValidateMessageMetadata(DbcMessage message, List<DbcDiagnostic> diagnostics)
    {
        if (message.Attributes.Count > 0)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' attribute values are not supported by the current normalized export.", diagnostics);
        }

        if (message.CycleTimeMs.HasValue)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' cycle time metadata is not supported by the current normalized export.", diagnostics);
        }

        if (message.SendType != DbcSendType.Unknown)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' send type metadata is not supported by the current normalized export.", diagnostics);
        }

        if (message.TimeoutTimeMs.HasValue)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' timeout metadata is not supported by the current normalized export.", diagnostics);
        }

        var unsupportedFrameFlags = message.FrameFlags & UnsupportedFrameFlags;
        if (message.DataLength <= 8)
        {
            unsupportedFrameFlags |= message.FrameFlags & DbcFrameFlags.FlexibleDataRate;
        }

        if (unsupportedFrameFlags != DbcFrameFlags.None)
        {
            AddUnsupportedMetadata($"Message '{message.Name}' frame flags '{unsupportedFrameFlags}' are not supported by the current normalized export.", diagnostics);
        }
    }

    private static void ValidateSignalMetadata(DbcMessage message, DbcSignal signal, List<DbcDiagnostic> diagnostics)
    {
        if (signal.Attributes.Count > 0)
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' attribute values are not supported by the current normalized export.", diagnostics);
        }

        if (signal.InitialValue.HasValue)
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' initial value metadata is not supported by the current normalized export.", diagnostics);
        }

        if (signal.SendType != DbcSendType.Unknown)
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' send type metadata is not supported by the current normalized export.", diagnostics);
        }

        if (signal.TimeoutTimeMs.HasValue)
        {
            AddUnsupportedMetadata($"Signal '{message.Name}.{signal.Name}' timeout metadata is not supported by the current normalized export.", diagnostics);
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
    }

    private static void ValidateLongSymbolExport(string objectKind, string canonicalName, string exportName, List<DbcDiagnostic> diagnostics)
    {
        if (string.Equals(exportName, canonicalName, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL",
            $"{objectKind} '{canonicalName}' would be exported as '{exportName}', but Task 2 normalized export does not emit Vector long-symbol attributes yet."));
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
