using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

internal static partial class DbcWriteValidator
{
    public static DbcValidationResult Validate(DbcDocument document, DbcWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DbcWriterOptions.Default;

        var diagnostics = new List<DbcDiagnostic>();
        ValidateObjectName("node", document.Nodes.Select(x => DbcWriterNameFormatter.GetNodeExportName(x, options)), diagnostics);
        ValidateObjectName("message", document.Messages.Select(x => x.Name), diagnostics);

        foreach (var message in document.Messages)
        {
            if (!IsValidIdentifier(message.SourceName))
            {
                diagnostics.Add(Error(
                    "DBC_WRITE_INVALID_IDENTIFIER",
                    $"Message '{message.Name}' source name '{message.SourceName}' is not a valid DBC identifier."));
            }

            if (!message.SupportsSingleFrameRuntime)
            {
                diagnostics.Add(new DbcDiagnostic(
                    DbcDiagnosticSeverity.Warning,
                    "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE",
                    $"Message '{message.Name}' payload length {message.DataLength} can be exported as metadata but is not supported by the CAN/CAN FD single-frame runtime."));
            }

            ValidateObjectName($"signal in message '{message.Name}'", message.Signals.Select(x => x.Name), diagnostics);
            foreach (var signal in message.Signals)
            {
                if (!IsValidIdentifier(signal.SourceName))
                {
                    diagnostics.Add(Error(
                        "DBC_WRITE_INVALID_IDENTIFIER",
                        $"Signal '{message.Name}.{signal.Name}' source name '{signal.SourceName}' is not a valid DBC identifier."));
                }
            }
        }

        return new DbcValidationResult(diagnostics);
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
