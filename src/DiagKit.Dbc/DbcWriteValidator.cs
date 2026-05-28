using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

internal static partial class DbcWriteValidator
{
    private const string EmptyReceiverSentinel = "Vector__XXX";
    private const int MaxCanFdSignalBitEnd = 512;

    public static DbcValidationResult Validate(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        ValidateObjectName("node", document.Nodes.Select(x => DbcWriterNameFormatter.GetNodeExportName(x, options)), diagnostics);
        ValidateObjectName("message", document.Messages.Select(x => DbcWriterNameFormatter.GetMessageExportName(x, options)), diagnostics);

        foreach (var message in document.Messages)
        {
            if (!message.SupportsSingleFrameRuntime)
            {
                diagnostics.Add(new DbcDiagnostic(
                    DbcDiagnosticSeverity.Warning,
                    "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE",
                    $"Message '{message.Name}' payload length {message.DataLength} can be exported as metadata but is not supported by the CAN/CAN FD single-frame runtime."));
            }

            var transmitterName = DbcWriterNameFormatter.GetNodeExportName(message.PrimaryTransmitter, options);
            if (!IsValidIdentifier(transmitterName))
            {
                diagnostics.Add(Error("DBC_WRITE_INVALID_IDENTIFIER", $"Message '{message.Name}' transmitter name '{transmitterName}' is not a valid DBC identifier."));
            }

            ValidateObjectName($"signal in message '{message.Name}'", message.Signals.Select(x => DbcWriterNameFormatter.GetSignalExportName(x, options)), diagnostics);
            foreach (var signal in message.Signals)
            {
                ValidateSignal(message, signal, diagnostics);

                foreach (var receiver in signal.Receivers)
                {
                    var receiverName = DbcWriterNameFormatter.GetNodeExportName(receiver, options);
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

    private static void ValidateSignalBitRange(DbcMessage message, DbcSignal signal, List<DbcDiagnostic> diagnostics)
    {
        var bitEnd = (long)signal.StartBit + signal.BitLength;
        if (signal.StartBit >= 0 &&
            signal.BitLength > 0 &&
            bitEnd <= MaxCanFdSignalBitEnd)
        {
            return;
        }

        diagnostics.Add(Error(
            "DBC_WRITE_INVALID_SIGNAL_BIT_RANGE",
            $"Signal '{message.Name}.{signal.Name}' bit range {signal.StartBit}|{signal.BitLength} cannot be exported as a single-frame DBC signal."));
    }

    private static bool HasUnsupportedMultiplexing(DbcMultiplexing multiplexing)
    {
        return multiplexing.SwitchRanges.Count > 0 ||
            !string.IsNullOrEmpty(multiplexing.MultiplexorSignalName) ||
            (multiplexing.Role == DbcMultiplexingRole.Multiplexed && multiplexing.SwitchValue is null);
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
