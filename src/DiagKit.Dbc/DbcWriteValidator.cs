using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

internal static partial class DbcWriteValidator
{
    private const string EmptyReceiverSentinel = "Vector__XXX";
    private const int MaxSignalBitLength = 64;

    public static DbcValidationResult Validate(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        ValidateObjectName("node", document.Nodes.Select(x => DbcWriterNameFormatter.GetNodeExportName(x, options)), diagnostics);
        ValidateObjectName("message", document.Messages.Select(x => DbcWriterNameFormatter.GetMessageExportName(x, options)), diagnostics);
        foreach (var node in document.Nodes)
        {
            ValidateLongSymbolExport("Node", node.Name, DbcWriterNameFormatter.GetNodeExportName(node, options), diagnostics);
        }

        foreach (var message in document.Messages)
        {
            ValidateLongSymbolExport("Message", message.Name, DbcWriterNameFormatter.GetMessageExportName(message, options), diagnostics);

            if (message.Transmitters.Count > 1)
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
            if (!IsValidIdentifier(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Message '{message.Name}' transmitter name '{transmitterName}' is not a valid DBC identifier."));
            }

            ValidateObjectName($"signal in message '{message.Name}'", message.Signals.Select(x => DbcWriterNameFormatter.GetSignalExportName(x, options)), diagnostics);
            foreach (var signal in message.Signals)
            {
                ValidateLongSymbolExport("Signal", signal.Name, DbcWriterNameFormatter.GetSignalExportName(signal, options), diagnostics);
                ValidateSignal(message, signal, diagnostics);

                foreach (var receiver in signal.Receivers)
                {
                    var receiverName = DbcWriterNameFormatter.GetNodeExportName(receiver, options);
                    ValidateLongSymbolExport("Node", receiver.Name, receiverName, diagnostics);
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

    private static void ValidateSignal(DbcMessage message, DbcSignal signal, List<DbcDiagnostic> diagnostics)
    {
        ValidateSignalBitRange(message, signal, diagnostics);

        if (signal.ValueType is DbcSignalValueType.Float or DbcSignalValueType.Double)
        {
            diagnostics.Add(Error(
                "DBC_WRITE_UNSUPPORTED_SIGNAL_VALUE_TYPE",
                $"Signal '{message.Name}.{signal.Name}' uses {signal.ValueType}, but Task 2 normalized export does not emit SIG_VALTYPE_ yet."));
        }

        if (HasUnsupportedMultiplexing(signal.Multiplexing))
        {
            diagnostics.Add(Error(
                "DBC_WRITE_UNSUPPORTED_MULTIPLEXING",
                $"Signal '{message.Name}.{signal.Name}' uses unsupported multiplexing for Task 2 normalized export."));
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

    private static bool HasUnsupportedMultiplexing(DbcMultiplexing multiplexing)
    {
        var hasExtendedFields = multiplexing.SwitchRanges.Count > 0 ||
            !string.IsNullOrEmpty(multiplexing.MultiplexorSignalName);

        return multiplexing.Role switch
        {
            DbcMultiplexingRole.None or DbcMultiplexingRole.Multiplexor => multiplexing.SwitchValue is not null || hasExtendedFields,
            DbcMultiplexingRole.Multiplexed => multiplexing.SwitchValue is not >= 0 || hasExtendedFields,
            _ => true,
        };
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

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DbcIdentifierRegex();
}
