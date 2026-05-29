using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagKit.Dbc;

/// <summary>
/// DBC 文件加载入口，返回不可变文档模型和结构化 diagnostics。<br/>
/// DBC file loading entry point, returning an immutable document model and structured diagnostics.
/// </summary>
public static partial class DbcLoader
{
    /// <summary>
    /// 从文件路径同步加载 DBC。<br/>
    /// Synchronously loads a DBC file from a path.
    /// </summary>
    public static DbcLoadResult LoadFile(string path, DbcLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadText(File.ReadAllText(path), options);
    }

    /// <summary>
    /// 从文件路径同步加载 DBC 文档，加载失败时抛出 DbcException。<br/>
    /// Synchronously loads a DBC document from a path, throwing DbcException on failure.
    /// </summary>
    public static DbcDocument LoadDocument(string path, DbcLoadOptions? options = null)
    {
        return LoadDocumentOrThrow(path, options);
    }

    /// <summary>
    /// 从文件路径同步加载 DBC 文档，加载失败时抛出 DbcException。<br/>
    /// Synchronously loads a DBC document from a path, throwing DbcException on failure.
    /// </summary>
    public static DbcDocument LoadDocumentOrThrow(string path, DbcLoadOptions? options = null)
    {
        return LoadFile(path, options).GetDocumentOrThrow();
    }

    /// <summary>
    /// 从文件路径异步加载 DBC。<br/>
    /// Asynchronously loads a DBC file from a path.
    /// </summary>
    public static async Task<DbcLoadResult> LoadFileAsync(string path, DbcLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return LoadText(text, options);
    }

    /// <summary>
    /// 从文件路径异步加载 DBC 文档，加载失败时抛出 DbcException。<br/>
    /// Asynchronously loads a DBC document from a path, throwing DbcException on failure.
    /// </summary>
    public static Task<DbcDocument> LoadDocumentAsync(
        string path,
        DbcLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return LoadDocumentOrThrowAsync(path, options, cancellationToken);
    }

    /// <summary>
    /// 从文件路径异步加载 DBC 文档，加载失败时抛出 DbcException。<br/>
    /// Asynchronously loads a DBC document from a path, throwing DbcException on failure.
    /// </summary>
    public static async Task<DbcDocument> LoadDocumentOrThrowAsync(
        string path,
        DbcLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await LoadFileAsync(path, options, cancellationToken).ConfigureAwait(false);
        return result.GetDocumentOrThrow();
    }

    /// <summary>
    /// 从 DBC 文本加载文档，适合测试、内存缓存或上层自定义文件系统。<br/>
    /// Loads a document from DBC text, suitable for testing, in-memory caching, or custom file systems.
    /// </summary>
    public static DbcLoadResult LoadText(string dbcText, DbcLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dbcText);
        options ??= DbcLoadOptions.Strict;

        var parser = new Parser(options);
        return parser.Parse(dbcText);
    }

    /// <summary>
    /// 从 DBC 文本加载文档，加载失败时抛出 DbcException。<br/>
    /// Loads a document from DBC text, throwing DbcException on failure.
    /// </summary>
    public static DbcDocument LoadTextDocument(string dbcText, DbcLoadOptions? options = null)
    {
        return LoadTextDocumentOrThrow(dbcText, options);
    }

    /// <summary>
    /// 从 DBC 文本加载文档，加载失败时抛出 DbcException。<br/>
    /// Loads a document from DBC text, throwing DbcException on failure.
    /// </summary>
    public static DbcDocument LoadTextDocumentOrThrow(string dbcText, DbcLoadOptions? options = null)
    {
        return LoadText(dbcText, options).GetDocumentOrThrow();
    }

    private sealed class Parser
    {
        private const string EmptyReceiverSentinel = "Vector__XXX";

        private readonly DbcLoadOptions options;
        private readonly List<DbcDiagnostic> diagnostics = [];
        private readonly Dictionary<string, NodeBuilder> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, MessageBuilder> messages = [];
        private readonly Dictionary<string, uint> messageRawIdsByName = new(StringComparer.Ordinal);
        private readonly Dictionary<CanIdentifier, uint> messageRawIdsByIdentifier = [];
        private readonly Dictionary<string, DbcAttributeDefinition> attributeDefinitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DbcAttributeValue> documentAttributes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<long, string>> namedValueTables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnvironmentVariableBuilder> environmentVariables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DbcRelationAttributeDefinition> relationAttributeDefinitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DbcRelationAttributeDefault> relationAttributeDefaults = new(StringComparer.Ordinal);
        private readonly List<DbcRelationAttributeValue> relationAttributes = [];
        private readonly Dictionary<(uint MessageId, string SignalName), SignalCommentBuilder> signalComments = [];
        private readonly Dictionary<(uint MessageId, string SignalName), SignalValueDescriptionBuilder> signalValueDescriptions = [];
        private readonly Dictionary<(uint MessageId, string SignalName), SignalValueTypeBuilder> signalValueTypes = [];
        private readonly HashSet<(uint MessageId, string SignalName, string MetadataKind)> ambiguousSignalMetadataDiagnostics = [];
        private readonly List<PendingAttributeDefault> pendingAttributeDefaults = [];
        private readonly List<PendingAttributeValue> pendingAttributeValues = [];
        private readonly List<ExtendedMultiplexingBuilder> extendedMultiplexingDefinitions = [];
        private string? documentComment;
        private MessageBuilder? currentMessage;
        private bool insideNamespaceList;

        public Parser(DbcLoadOptions options)
        {
            this.options = options;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxStatementLength);
        }

        public DbcLoadResult Parse(string text)
        {
            foreach (var statement in ReadStatements(text))
            {
                if (statement.HasUnterminatedQuote)
                {
                    AddError("DBC_UNTERMINATED_QUOTED_STATEMENT", $"Unterminated quoted DBC statement: {statement.Text}", statement.LineNumber);
                    continue;
                }

                try
                {
                    ParseLine(statement.Text, statement.LineNumber);
                }
                catch (DbcNumericParseException ex)
                {
                    if (statement.Text.TrimStart().StartsWith("BO_ ", StringComparison.Ordinal))
                    {
                        currentMessage = null;
                    }

                    AddRecoverableDiagnostic(
                        "DBC_NUMERIC_PARSE",
                        $"DBC statement contains an invalid or out-of-range numeric value: {ex.Message}",
                        statement.LineNumber);
                }
            }

            ApplyPendingAttributes();

            DbcDocument? document = null;
            if (!HasFatalErrors && (options.Mode == DbcLoadMode.Lenient || !HasErrors))
            {
                try
                {
                    document = BuildDocument();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    AddError("DBC_BUILD_FAILED", ex.Message, 0);
                }
            }

            if ((options.Mode == DbcLoadMode.Strict && HasErrors) || HasFatalErrors)
            {
                document = null;
            }

            return new DbcLoadResult(document, diagnostics);
        }

        private bool HasErrors
        {
            get
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Severity == DbcDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void ParseLine(string rawLine, int lineNumber)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("VERSION", StringComparison.Ordinal))
            {
                return;
            }

            if (NamespaceListHeaderRegex().IsMatch(line))
            {
                insideNamespaceList = true;
                return;
            }

            if (insideNamespaceList)
            {
                if (IsNamespaceListEntry(line))
                {
                    return;
                }

                insideNamespaceList = false;
            }

            if (line.StartsWith("NS_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_NAMESPACE_SYNTAX", $"Invalid namespace declaration line: {line}", lineNumber);
                return;
            }

            if (line.StartsWith("BS_", StringComparison.Ordinal))
            {
                return;
            }

            if (TryParseNodes(line, lineNumber)) return;
            if (line.StartsWith("BO_ ", StringComparison.Ordinal))
            {
                TryParseMessage(line, lineNumber);
                return;
            }

            if (line.StartsWith("SG_ ", StringComparison.Ordinal))
            {
                TryParseSignal(line, lineNumber);
                return;
            }

            if (TryParseAdditionalMessageTransmitters(line, lineNumber)) return;
            if (TryParseEnvironmentVariable(line, lineNumber)) return;
            if (TryParseExtendedMultiplexing(line, lineNumber)) return;
            if (TryParseComment(line, lineNumber)) return;
            if (TryParseRelationAttributeDefinition(line, lineNumber)) return;
            if (TryParseRelationAttributeDefault(line, lineNumber)) return;
            if (TryParseRelationAttributeValue(line, lineNumber)) return;
            if (TryParseAttributeDefinition(line, lineNumber)) return;
            if (TryParseAttributeDefault(line, lineNumber)) return;
            if (TryParseAttributeValue(line, lineNumber)) return;
            if (TryParseValueTable(line, lineNumber)) return;
            if (TryParseValueDescription(line, lineNumber)) return;
            if (TryParseSignalValueType(line, lineNumber)) return;

            if (TryAddMalformedKnownKeywordDiagnostic(line, lineNumber))
            {
                return;
            }

            if (IsNamespaceListEntry(line))
            {
                AddRecoverableDiagnostic("DBC_NAMESPACE_ENTRY_OUTSIDE_NAMESPACE", $"Namespace entry appeared outside an NS_ list: {line}", lineNumber);
                return;
            }

            if (line.EndsWith(':') || line.EndsWith(';'))
            {
                AddWarning("DBC_UNSUPPORTED_LINE", $"Unsupported DBC line was skipped: {line}", lineNumber);
                return;
            }
        }

        private bool HasFatalErrors
        {
            get
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Severity == DbcDiagnosticSeverity.Error &&
                        diagnostic.Code == "DBC_NUMERIC_PARSE")
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static bool IsNamespaceListEntry(string line)
        {
            return line switch
            {
                "NS_DESC_" or
                "CM_" or
                "BA_DEF_" or
                "BA_" or
                "VAL_" or
                "CAT_DEF_" or
                "CAT_" or
                "FILTER" or
                "BA_DEF_DEF_" or
                "EV_DATA_" or
                "ENVVAR_DATA_" or
                "SGTYPE_" or
                "SGTYPE_VAL_" or
                "BA_DEF_SGTYPE_" or
                "BA_SGTYPE_" or
                "SIG_TYPE_REF_" or
                "VAL_TABLE_" or
                "SIG_GROUP_" or
                "SIG_VALTYPE_" or
                "SIGTYPE_VALTYPE_" or
                "BO_TX_BU_" or
                "BA_DEF_REL_" or
                "BA_REL_" or
                "BA_DEF_DEF_REL_" or
                "BU_SG_REL_" or
                "BU_EV_REL_" or
                "BU_BO_REL_" or
                "SG_MUL_VAL_" => true,
                _ => false,
            };
        }

        private bool TryParseNodes(string line, int lineNumber)
        {
            if (!line.StartsWith("BU_:", StringComparison.Ordinal))
            {
                return false;
            }

            var names = line[4..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0)
            {
                AddRecoverableDiagnostic("DBC_NODE_LIST_EMPTY", "BU_: node list is empty.", lineNumber);
                return true;
            }

            foreach (var name in names)
            {
                GetOrAddNode(name);
            }

            return true;
        }

        private bool TryParseMessage(string line, int lineNumber)
        {
            var match = MessageRegex().Match(line);
            if (!match.Success)
            {
                currentMessage = null;
                AddRecoverableDiagnostic("DBC_MESSAGE_SYNTAX", $"Invalid message line: {line}", lineNumber);
                return false;
            }

            var rawId = ParseUInt32(match.Groups["id"].Value);
            var name = match.Groups["name"].Value;
            var dataLength = ParseInt32(match.Groups["length"].Value);
            var transmitter = match.Groups["tx"].Value;

            if (dataLength < 0)
            {
                AddError("DBC_MESSAGE_LENGTH_OUT_OF_RANGE", $"Message '{name}' declares negative payload length {dataLength}.", lineNumber);
                currentMessage = null;
                return true;
            }

            if (dataLength > 64)
            {
                AddRecoverableDiagnostic(
                    "DBC_MESSAGE_RUNTIME_UNSUPPORTED",
                    $"Message '{name}' declares payload length {dataLength}; it can be preserved as DBC metadata but is not supported by the current CAN/CAN FD single-frame runtime.",
                    lineNumber);
            }

            if (messages.TryGetValue(rawId, out var existingMessage))
            {
                AddError("DBC_DUPLICATE_MESSAGE_ID", $"Duplicate message raw id '{rawId}'.", lineNumber);
                currentMessage = existingMessage;
                return true;
            }

            var identifier = new DbcRawMessageId(rawId).ToCanIdentifier();
            if (messageRawIdsByIdentifier.TryGetValue(identifier, out var existingRawId))
            {
                AddError(
                    "DBC_DUPLICATE_CAN_IDENTIFIER",
                    $"Duplicate normalized CAN identifier '{identifier}'. Raw id '{rawId}' conflicts with raw id '{existingRawId}'.",
                    lineNumber);
                currentMessage = null;
                return true;
            }

            if (messageRawIdsByName.ContainsKey(name))
            {
                AddRecoverableDiagnostic("DBC_DUPLICATE_MESSAGE_NAME", $"Duplicate message name '{name}'.", lineNumber);
                currentMessage = null;
                return true;
            }

            var message = new MessageBuilder(rawId, name, dataLength, transmitter, lineNumber);
            messages.Add(rawId, message);
            messageRawIdsByIdentifier.Add(identifier, rawId);
            messageRawIdsByName.Add(name, rawId);

            currentMessage = message;
            GetOrAddNode(transmitter);
            return true;
        }

        private bool TryParseSignal(string line, int lineNumber)
        {
            var match = SignalRegex().Match(line);
            if (!match.Success)
            {
                AddRecoverableDiagnostic("DBC_SIGNAL_SYNTAX", $"Invalid signal line: {line}", lineNumber);
                return false;
            }

            if (currentMessage is null)
            {
                AddRecoverableDiagnostic("DBC_SIGNAL_WITHOUT_MESSAGE", $"Signal line appeared before any message: {line}", lineNumber);
                return true;
            }

            var receivers = match.Groups["rx"].Value
                .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Where(static receiver => !string.Equals(receiver, EmptyReceiverSentinel, StringComparison.Ordinal))
                .ToArray();

            foreach (var receiver in receivers)
            {
                GetOrAddNode(receiver);
            }

            var multiplexingText = match.Groups["mux"].Value;
            var signal = new SignalBuilder(
                match.Groups["name"].Value,
                ParseInt32(match.Groups["start"].Value),
                ParseInt32(match.Groups["length"].Value),
                match.Groups["order"].Value == "0" ? DbcByteOrder.Motorola : DbcByteOrder.Intel,
                match.Groups["sign"].Value == "-" ? DbcSignalValueType.Signed : DbcSignalValueType.Unsigned,
                ParseDouble(match.Groups["factor"].Value),
                ParseDouble(match.Groups["offset"].Value),
                ParseDouble(match.Groups["min"].Value),
                ParseDouble(match.Groups["max"].Value),
                DbcQuotedText.Unescape(match.Groups["unit"].Value),
                receivers,
                ParseMultiplexing(multiplexingText),
                lineNumber);

            foreach (var existingSignal in currentMessage.Signals)
            {
                if (string.Equals(existingSignal.Name, signal.Name, StringComparison.Ordinal))
                {
                    AddRecoverableDiagnostic("DBC_DUPLICATE_SIGNAL_NAME", $"Message '{currentMessage.Name}' contains duplicate signal '{signal.Name}'.", lineNumber);
                    break;
                }
            }

            currentMessage.Signals.Add(signal);
            return true;
        }

        private bool TryParseAdditionalMessageTransmitters(string line, int lineNumber)
        {
            var match = AdditionalMessageTransmittersRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var rawId = ParseUInt32(match.Groups["id"].Value);
            if (!messages.TryGetValue(rawId, out var message))
            {
                AddWarning("DBC_TRANSMITTER_TARGET_MISSING", $"BO_TX_BU_ refers to missing message '{rawId}'.", lineNumber);
                return true;
            }

            var transmitterNames = match.Groups["nodes"].Value
                .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var transmitterName in transmitterNames)
            {
                GetOrAddNode(transmitterName);
                if (!message.Transmitters.Contains(transmitterName, StringComparer.Ordinal))
                {
                    message.Transmitters.Add(transmitterName);
                }
            }

            return true;
        }

        private bool TryParseEnvironmentVariable(string line, int lineNumber)
        {
            var match = EnvironmentVariableRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = match.Groups["name"].Value;
            if (environmentVariables.ContainsKey(name))
            {
                AddRecoverableDiagnostic("DBC_DUPLICATE_ENVIRONMENT_VARIABLE", $"Duplicate environment variable '{name}' was skipped.", lineNumber);
                return true;
            }

            var accessNodes = match.Groups["nodes"].Success
                ? match.Groups["nodes"].Value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();
            foreach (var nodeName in accessNodes)
            {
                GetOrAddNode(nodeName);
            }

            environmentVariables.Add(name, new EnvironmentVariableBuilder(
                name,
                ParseInt32(match.Groups["type"].Value),
                ParseDouble(match.Groups["min"].Value),
                ParseDouble(match.Groups["max"].Value),
                DbcQuotedText.Unescape(match.Groups["unit"].Value),
                ParseDouble(match.Groups["initial"].Value),
                ParseInt32(match.Groups["id"].Value),
                match.Groups["accessType"].Value,
                accessNodes,
                lineNumber));
            return true;
        }

        private bool TryParseRelationAttributeDefinition(string line, int lineNumber)
        {
            var match = RelationAttributeDefinitionRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = DbcQuotedText.Unescape(match.Groups["name"].Value);
            if (relationAttributeDefinitions.ContainsKey(name))
            {
                AddRecoverableDiagnostic("DBC_DUPLICATE_RELATION_ATTRIBUTE_DEFINITION", $"Duplicate relation attribute definition '{name}' was skipped.", lineNumber);
                return true;
            }

            var valueKind = ParseAttributeValueKind(match.Groups["kind"].Value);
            var enumValues = valueKind == DbcAttributeValueKind.Enum
                ? ParseQuotedValues(match.Groups["enum"].Value)
                : null;
            relationAttributeDefinitions.Add(name, new DbcRelationAttributeDefinition(
                name,
                match.Groups["relation"].Value,
                valueKind,
                enumValues,
                TryParseOptionalDouble(match.Groups["min"].Value),
                TryParseOptionalDouble(match.Groups["max"].Value),
                lineNumber));
            return true;
        }

        private bool TryParseRelationAttributeDefault(string line, int lineNumber)
        {
            var match = RelationAttributeDefaultRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = DbcQuotedText.Unescape(match.Groups["name"].Value);
            relationAttributeDefaults[name] = new DbcRelationAttributeDefault(name, Unquote(match.Groups["value"].Value), lineNumber);
            return true;
        }

        private bool TryParseRelationAttributeValue(string line, int lineNumber)
        {
            var match = RelationAttributeValueRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = DbcQuotedText.Unescape(match.Groups["name"].Value);
            relationAttributes.Add(new DbcRelationAttributeValue(
                name,
                NormalizeWhitespace(match.Groups["target"].Value),
                Unquote(match.Groups["value"].Value),
                lineNumber));
            AddWarning(
                "DBC_RELATION_ATTRIBUTE_UNAPPLIED",
                $"Relation attribute '{name}' was preserved as metadata but not applied to message/signal models.",
                lineNumber);
            return true;
        }

        private bool TryParseComment(string line, int lineNumber)
        {
            var documentComment = DocumentCommentRegex().Match(line);
            if (documentComment.Success)
            {
                this.documentComment = DbcQuotedText.Unescape(documentComment.Groups["text"].Value);
                return true;
            }

            var messageComment = MessageCommentRegex().Match(line);
            if (messageComment.Success)
            {
                var rawId = ParseUInt32(messageComment.Groups["id"].Value);
                if (messages.TryGetValue(rawId, out var message))
                {
                    message.Comment = DbcQuotedText.Unescape(messageComment.Groups["text"].Value);
                }
                else
                {
                    AddWarning("DBC_COMMENT_TARGET_MISSING", $"Message comment target '{rawId}' was not found.", lineNumber);
                }

                return true;
            }

            var legacyMessageComment = LegacyMessageCommentRegex().Match(line);
            if (legacyMessageComment.Success)
            {
                var rawId = ParseUInt32(legacyMessageComment.Groups["id"].Value);
                if (messages.TryGetValue(rawId, out var message))
                {
                    message.Comment = DbcQuotedText.Unescape(legacyMessageComment.Groups["text"].Value);
                }
                else
                {
                    AddWarning("DBC_COMMENT_TARGET_MISSING", $"Message comment target '{rawId}' was not found.", lineNumber);
                }

                return true;
            }

            var signalComment = SignalCommentRegex().Match(line);
            if (signalComment.Success)
            {
                signalComments[(ParseUInt32(signalComment.Groups["id"].Value), signalComment.Groups["signal"].Value)] =
                    new SignalCommentBuilder(DbcQuotedText.Unescape(signalComment.Groups["text"].Value), lineNumber);
                return true;
            }

            var nodeComment = NodeCommentRegex().Match(line);
            if (nodeComment.Success)
            {
                GetOrAddNode(nodeComment.Groups["node"].Value).Comment = DbcQuotedText.Unescape(nodeComment.Groups["text"].Value);
                return true;
            }

            if (line.StartsWith("CM_ ", StringComparison.Ordinal))
            {
                AddWarning("DBC_COMMENT_UNSUPPORTED", $"Unsupported or invalid comment line was skipped: {line}", lineNumber);
                return true;
            }

            return false;
        }

        private bool TryParseAttributeDefinition(string line, int lineNumber)
        {
            var match = AttributeDefinitionRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var ownerKind = ParseOwnerKind(match.Groups["owner"].Value);
            var valueKind = ParseAttributeValueKind(match.Groups["kind"].Value);
            IReadOnlyList<string>? enumValues = null;
            if (valueKind == DbcAttributeValueKind.Enum)
            {
                enumValues = ParseQuotedValues(match.Groups["enum"].Value);
            }

            var name = DbcQuotedText.Unescape(match.Groups["name"].Value);
            if (attributeDefinitions.ContainsKey(name))
            {
                AddRecoverableDiagnostic("DBC_DUPLICATE_ATTRIBUTE_DEFINITION", $"Duplicate attribute definition '{name}' was skipped.", lineNumber);
                return true;
            }

            attributeDefinitions.Add(name, new DbcAttributeDefinition(
                name,
                ownerKind,
                valueKind,
                enumValues,
                TryParseOptionalDouble(match.Groups["min"].Value),
                TryParseOptionalDouble(match.Groups["max"].Value),
                sourceLine: lineNumber));

            return true;
        }

        private bool TryParseAttributeDefault(string line, int lineNumber)
        {
            var match = AttributeDefaultRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = DbcQuotedText.Unescape(match.Groups["name"].Value);
            pendingAttributeDefaults.Add(new PendingAttributeDefault(name, match.Groups["value"].Value, lineNumber));
            return true;
        }

        private bool TryParseAttributeValue(string line, int lineNumber)
        {
            var match = AttributeValueRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            if (match.Groups["owner"].Value.Length == 0 &&
                IsAttributeOwnerToken(match.Groups["value"].Value.AsSpan()))
            {
                return false;
            }

            if (HasMissingWhitespaceAfterQuotedAttributeName(line))
            {
                AddRecoverableDiagnostic(
                    "DBC_ATTRIBUTE_OWNER_WHITESPACE",
                    $"Attribute value '{DbcQuotedText.Unescape(match.Groups["name"].Value)}' omits whitespace before its owner token.",
                    lineNumber);
            }

            pendingAttributeValues.Add(CreatePendingAttributeValue(match, lineNumber));
            return true;
        }

        private static PendingAttributeValue CreatePendingAttributeValue(Match match, int lineNumber)
        {
            return new PendingAttributeValue(
                DbcQuotedText.Unescape(match.Groups["name"].Value),
                match.Groups["value"].Value,
                match.Groups["owner"].Value,
                match.Groups["id"].Value,
                match.Groups["signal"].Value,
                match.Groups["node"].Value,
                match.Groups["env"].Value,
                lineNumber);
        }

        private void ApplyAttributeValue(DbcAttributeDefinition definition, PendingAttributeValue pending)
        {
            var value = CreateAttributeValue(definition, pending.RawValue, pending.SourceLine);
            var owner = pending.Owner;
            if (owner.Length == 0)
            {
                documentAttributes[pending.AttributeName] = value;
            }
            else if (string.Equals(owner, "BO_", StringComparison.Ordinal))
            {
                var rawId = ParseUInt32(pending.RawId);
                if (messages.TryGetValue(rawId, out var message))
                {
                    message.Attributes[pending.AttributeName] = value;
                    ApplyMessageAttribute(pending.AttributeName, value, message);
                }
                else
                {
                    AddError("DBC_ATTRIBUTE_TARGET_MISSING", $"Attribute '{pending.AttributeName}' refers to missing message '{rawId}'.", pending.SourceLine);
                }
            }
            else if (string.Equals(owner, "SG_", StringComparison.Ordinal))
            {
                var rawId = ParseUInt32(pending.RawId);
                var signalName = pending.SignalName;
                if (messages.TryGetValue(rawId, out var message))
                {
                    if (TryFindUniqueSignal(message, signalName, pending.SourceLine, "attribute value", out var signal))
                    {
                        signal.Attributes[pending.AttributeName] = value;
                        ApplySignalAttribute(pending.AttributeName, value, signal);
                    }
                    else if (!AnySignal(message, signalName))
                    {
                        AddError("DBC_ATTRIBUTE_TARGET_MISSING", $"Attribute '{pending.AttributeName}' refers to missing signal '{signalName}' in message '{rawId}'.", pending.SourceLine);
                    }
                }
                else
                {
                    AddError("DBC_ATTRIBUTE_TARGET_MISSING", $"Attribute '{pending.AttributeName}' refers to missing message '{rawId}'.", pending.SourceLine);
                }
            }
            else if (string.Equals(owner, "BU_", StringComparison.Ordinal))
            {
                GetOrAddNode(pending.NodeName).Attributes[pending.AttributeName] = value;
            }
            else if (string.Equals(owner, "EV_", StringComparison.Ordinal))
            {
                if (environmentVariables.TryGetValue(pending.EnvName, out var environmentVariable))
                {
                    environmentVariable.Attributes[pending.AttributeName] = value;
                }
                else
                {
                    AddError("DBC_ATTRIBUTE_TARGET_MISSING", $"Attribute '{pending.AttributeName}' refers to missing environment variable '{pending.EnvName}'.", pending.SourceLine);
                }
            }
        }

        private bool TryParseValueTable(string line, int lineNumber)
        {
            var match = ValueTableRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var name = match.Groups["name"].Value;
            var values = ParseValueDescriptionMap(match.Groups["values"].Value);
            if (namedValueTables.TryGetValue(name, out var existingValues))
            {
                if (options.Mode == DbcLoadMode.Strict)
                {
                    AddError("DBC_DUPLICATE_VALUE_TABLE", $"Duplicate value table '{name}'.", lineNumber);
                }
                else if (ValueDescriptionMapsEqual(existingValues, values))
                {
                    AddWarning("DBC_DUPLICATE_VALUE_TABLE", $"Duplicate equivalent value table '{name}' was skipped.", lineNumber);
                }
                else
                {
                    AddWarning("DBC_DUPLICATE_VALUE_TABLE", $"Duplicate value table '{name}' has conflicting content; the first definition was kept.", lineNumber);
                }

                return true;
            }

            namedValueTables[name] = values;
            return true;
        }

        private bool TryParseValueDescription(string line, int lineNumber)
        {
            var match = ValueDescriptionRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var rawId = ParseUInt32(match.Groups["id"].Value);
            var signalName = match.Groups["signal"].Value;
            var valuesText = match.Groups["values"].Value.Trim();
            if (ValueTableReferenceRegex().IsMatch(valuesText))
            {
                if (namedValueTables.TryGetValue(valuesText, out var namedValues))
                {
                    signalValueDescriptions[(rawId, signalName)] = new SignalValueDescriptionBuilder(new Dictionary<long, string>(namedValues), lineNumber);
                }
                else
                {
                    AddMissingValueTable(valuesText, lineNumber);
                }

                return true;
            }

            signalValueDescriptions[(rawId, signalName)] = new SignalValueDescriptionBuilder(ParseValueDescriptionMap(valuesText), lineNumber);
            return true;
        }

        private bool TryParseSignalValueType(string line, int lineNumber)
        {
            var match = SignalValueTypeRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var type = match.Groups["type"].Value switch
            {
                "1" => DbcSignalValueType.Float,
                "2" => DbcSignalValueType.Double,
                _ => DbcSignalValueType.Unsigned,
            };
            signalValueTypes[(ParseUInt32(match.Groups["id"].Value), match.Groups["signal"].Value)] = new SignalValueTypeBuilder(type, lineNumber);
            return true;
        }

        private bool TryParseExtendedMultiplexing(string line, int lineNumber)
        {
            const string keyword = "SG_MUL_VAL_";
            if (!line.StartsWith(keyword, StringComparison.Ordinal) ||
                line.Length == keyword.Length ||
                !char.IsWhiteSpace(line[keyword.Length]))
            {
                return false;
            }

            var match = ExtendedMultiplexingRegex().Match(line);
            if (match.Success)
            {
                if (!TryParseMultiplexorRanges(match.Groups["ranges"].Value, out var ranges))
                {
                    AddRecoverableDiagnostic(
                        "DBC_EXTENDED_MULTIPLEXING_RANGE",
                        $"Invalid extended multiplexing range list was skipped: {line}",
                        lineNumber);
                    return true;
                }

                extendedMultiplexingDefinitions.Add(new ExtendedMultiplexingBuilder(
                    ParseUInt32(match.Groups["id"].Value),
                    match.Groups["signal"].Value,
                    match.Groups["multiplexor"].Value,
                    ranges,
                    lineNumber));
                return true;
            }

            AddWarning(
                "DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED",
                $"Unsupported extended multiplexing definition was skipped: {line}",
                lineNumber);
            return true;
        }

        private void ApplyPendingAttributes()
        {
            foreach (var pending in pendingAttributeDefaults)
            {
                if (!attributeDefinitions.TryGetValue(pending.AttributeName, out var definition))
                {
                    AddAttributeReferenceMissing("Default value refers to unknown attribute", pending.AttributeName, pending.SourceLine);
                    continue;
                }

                try
                {
                    definition.DefaultValue = CreateAttributeValue(definition, pending.RawValue, pending.SourceLine);
                }
                catch (DbcNumericParseException ex)
                {
                    AddError(
                        "DBC_NUMERIC_PARSE",
                        $"DBC statement contains an invalid or out-of-range numeric value: {ex.Message}",
                        pending.SourceLine);
                }
            }

            foreach (var pending in pendingAttributeValues)
            {
                if (!attributeDefinitions.TryGetValue(pending.AttributeName, out var definition))
                {
                    AddAttributeReferenceMissing("Attribute value refers to unknown attribute", pending.AttributeName, pending.SourceLine);
                    continue;
                }

                try
                {
                    ApplyAttributeValue(definition, pending);
                }
                catch (DbcNumericParseException ex)
                {
                    AddError(
                        "DBC_NUMERIC_PARSE",
                        $"DBC statement contains an invalid or out-of-range numeric value: {ex.Message}",
                        pending.SourceLine);
                }
            }
        }

        private DbcDocument BuildDocument()
        {
            ApplyExtendedMultiplexingDefinitions();

            var nodeMap = new Dictionary<string, DbcNode>(StringComparer.Ordinal);
            foreach (var node in nodes.Values)
            {
                var nameParts = ResolveVectorLongName(node.Name, node.Attributes, "SystemNodeLongSymbol");
                nodeMap[node.Name] = new DbcNode(
                    nameParts.Name,
                    node.Comment,
                    new Dictionary<string, DbcAttributeValue>(node.Attributes, StringComparer.Ordinal),
                    nameParts.SourceName,
                    nameParts.NameAliases);
            }

            var builtEnvironmentVariables = new Dictionary<string, DbcEnvironmentVariable>(environmentVariables.Count, StringComparer.Ordinal);
            foreach (var sourceVariable in environmentVariables.Values)
            {
                var nameParts = ResolveVectorLongName(sourceVariable.Name, sourceVariable.Attributes, "SystemEnvVarLongSymbol");
                var accessNodes = new List<DbcNode>(sourceVariable.AccessNodes.Length);
                foreach (var nodeName in sourceVariable.AccessNodes)
                {
                    accessNodes.Add(nodeMap[nodeName]);
                }

                builtEnvironmentVariables.Add(sourceVariable.Name, new DbcEnvironmentVariable(
                    nameParts.Name,
                    sourceVariable.ValueType,
                    sourceVariable.Minimum,
                    sourceVariable.Maximum,
                    sourceVariable.Unit,
                    sourceVariable.InitialValue,
                    sourceVariable.Identifier,
                    sourceVariable.AccessType,
                    accessNodes,
                    sourceVariable.SourceLine,
                    new Dictionary<string, DbcAttributeValue>(sourceVariable.Attributes, StringComparer.Ordinal),
                    nameParts.SourceName,
                    nameParts.NameAliases));
            }

            var builtMessages = new List<DbcMessage>(messages.Count);
            foreach (var sourceMessage in messages.Values)
            {
                var duplicateSignalNames = GetDuplicateSignalNames(sourceMessage);
                var builtSignals = new List<DbcSignal>(sourceMessage.Signals.Count);
                foreach (var sourceSignal in sourceMessage.Signals)
                {
                    var nameParts = ResolveVectorLongName(sourceSignal.Name, sourceSignal.Attributes, "SystemSignalLongSymbol");
                    var receivers = new List<DbcNode>(sourceSignal.Receivers.Length);
                    foreach (var receiver in sourceSignal.Receivers)
                    {
                        receivers.Add(nodeMap[receiver]);
                    }

                    var key = (sourceMessage.RawId, sourceSignal.Name);
                    var hasAmbiguousName = duplicateSignalNames.Contains(sourceSignal.Name);
                    var valueType = sourceSignal.ValueType;
                    if (signalValueTypes.TryGetValue(key, out var overriddenValueType))
                    {
                        if (hasAmbiguousName)
                        {
                            ReportAmbiguousSignalMetadata(sourceMessage, sourceSignal.Name, "value type", overriddenValueType.SourceLine);
                        }
                        else
                        {
                            valueType = overriddenValueType.ValueType;
                        }
                    }

                    IReadOnlyDictionary<long, string>? valueDescriptions = null;
                    if (signalValueDescriptions.TryGetValue(key, out var descriptions))
                    {
                        if (hasAmbiguousName)
                        {
                            ReportAmbiguousSignalMetadata(sourceMessage, sourceSignal.Name, "value descriptions", descriptions.SourceLine);
                        }
                        else
                        {
                            valueDescriptions = descriptions.Values;
                        }
                    }

                    string? comment = null;
                    if (signalComments.TryGetValue(key, out var signalComment))
                    {
                        if (hasAmbiguousName)
                        {
                            ReportAmbiguousSignalMetadata(sourceMessage, sourceSignal.Name, "comment", signalComment.SourceLine);
                        }
                        else
                        {
                            comment = signalComment.Text;
                        }
                    }

                    builtSignals.Add(new DbcSignal(
                        nameParts.Name,
                        sourceSignal.StartBit,
                        sourceSignal.BitLength,
                        sourceSignal.ByteOrder,
                        valueType,
                        sourceSignal.Factor,
                        sourceSignal.Offset,
                        sourceSignal.Minimum,
                        sourceSignal.Maximum,
                        sourceSignal.Unit,
                        receivers,
                        sourceSignal.Multiplexing,
                        valueDescriptions,
                        new Dictionary<string, DbcAttributeValue>(sourceSignal.Attributes, StringComparer.Ordinal),
                        comment,
                        sourceSignal.InitialValue,
                        sourceSignal.SourceLine,
                        sourceSignal.SendType ?? GetDefaultSendType("GenSigSendType"),
                        sourceSignal.TimeoutTimeMs ?? GetDefaultInt32("GenSigTimeoutTime"),
                        nameParts.SourceName,
                        nameParts.NameAliases));
                }

                var messageNameParts = ResolveVectorLongName(sourceMessage.Name, sourceMessage.Attributes, "SystemMessageLongSymbol");
                var transmitterNodes = new List<DbcNode>(sourceMessage.Transmitters.Count);
                foreach (var transmitter in sourceMessage.Transmitters)
                {
                    var transmitterNode = nodeMap[transmitter];
                    if (!transmitterNodes.Contains(transmitterNode))
                    {
                        transmitterNodes.Add(transmitterNode);
                    }
                }

                builtMessages.Add(new DbcMessage(
                    new DbcRawMessageId(sourceMessage.RawId),
                    messageNameParts.Name,
                    sourceMessage.DataLength,
                    nodeMap[sourceMessage.Transmitter],
                    builtSignals,
                    transmitterNodes,
                    new Dictionary<string, DbcAttributeValue>(sourceMessage.Attributes, StringComparer.Ordinal),
                    sourceMessage.Comment,
                    sourceMessage.CycleTimeMs,
                    sourceMessage.FrameFlags,
                    sourceMessage.SourceLine,
                    sourceMessage.SendType ?? GetDefaultSendType("GenMsgSendType"),
                    sourceMessage.TimeoutTimeMs ?? GetDefaultInt32("GenMsgTimeoutTime"),
                    messageNameParts.SourceName,
                    messageNameParts.NameAliases));
            }

            ReportAmbiguousLookupNames("node", nodeMap.Values, node => node.Name, node => node.NameAliases);
            ReportAmbiguousLookupNames("message", builtMessages, message => message.Name, message => message.NameAliases);
            foreach (var message in builtMessages)
            {
                ReportAmbiguousLookupNames(
                    $"signal in message '{message.Name}'",
                    message.Signals,
                    signal => signal.Name,
                    signal => signal.NameAliases);
            }

            ReportAmbiguousLookupNames(
                "environment variable",
                builtEnvironmentVariables.Values,
                variable => variable.Name,
                variable => variable.NameAliases);

            return new DbcDocument(
                nodeMap.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray(),
                builtMessages,
                new Dictionary<string, DbcAttributeDefinition>(attributeDefinitions, StringComparer.Ordinal),
                new Dictionary<string, DbcAttributeValue>(documentAttributes, StringComparer.Ordinal),
                documentComment,
                builtEnvironmentVariables,
                new Dictionary<string, DbcRelationAttributeDefinition>(relationAttributeDefinitions, StringComparer.Ordinal),
                new Dictionary<string, DbcRelationAttributeDefault>(relationAttributeDefaults, StringComparer.Ordinal),
                relationAttributes.ToArray());
        }

        private static NameParts ResolveVectorLongName(
            string sourceName,
            IReadOnlyDictionary<string, DbcAttributeValue> attributes,
            string longSymbolAttributeName)
        {
            if (attributes.TryGetValue(longSymbolAttributeName, out var attribute) &&
                attribute.Value is string longName &&
                !string.IsNullOrWhiteSpace(longName) &&
                !string.Equals(longName, sourceName, StringComparison.Ordinal))
            {
                return new NameParts(longName, sourceName, [sourceName]);
            }

            return new NameParts(sourceName, sourceName, []);
        }

        private void ReportAmbiguousLookupNames<T>(
            string objectKind,
            IEnumerable<T> items,
            Func<T, string> getName,
            Func<T, IReadOnlyList<string>> getAliases)
            where T : class
        {
            var lookup = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                foreach (var lookupName in DbcNameLookup.EnumerateLookupNames(getName(item), getAliases(item)))
                {
                    if (!lookup.TryGetValue(lookupName, out var matches))
                    {
                        matches = [];
                        lookup.Add(lookupName, matches);
                    }

                    if (!matches.Contains(item))
                    {
                        matches.Add(item);
                    }
                }
            }

            foreach (var (lookupName, matches) in lookup)
            {
                if (matches.Count > 1)
                {
                    AddRecoverableDiagnostic(
                        "DBC_NAME_ALIAS_AMBIGUOUS",
                        $"Name '{lookupName}' resolves to multiple {objectKind} objects; name-based lookup for this name will fail closed.",
                        0);
                }
            }
        }

        private void ApplyExtendedMultiplexingDefinitions()
        {
            foreach (var definition in extendedMultiplexingDefinitions)
            {
                if (!messages.TryGetValue(definition.MessageId, out var message))
                {
                    AddExtendedMultiplexingReferenceMissing(
                        $"Extended multiplexing refers to missing message raw id '{definition.MessageId}'.",
                        definition.SourceLine);
                    continue;
                }

                if (!TryFindUniqueSignal(message, definition.SignalName, definition.SourceLine, "extended multiplexing target", out var signal))
                {
                    if (!AnySignal(message, definition.SignalName))
                    {
                        AddExtendedMultiplexingReferenceMissing(
                            $"Extended multiplexing refers to missing signal '{definition.SignalName}' in message '{message.Name}'.",
                            definition.SourceLine);
                    }

                    continue;
                }

                if (!TryFindUniqueSignal(message, definition.MultiplexorSignalName, definition.SourceLine, "extended multiplexing multiplexor", out var multiplexor))
                {
                    if (!AnySignal(message, definition.MultiplexorSignalName))
                    {
                        AddExtendedMultiplexingReferenceMissing(
                            $"Extended multiplexing refers to missing multiplexor signal '{definition.MultiplexorSignalName}' in message '{message.Name}'.",
                            definition.SourceLine);
                    }

                    continue;
                }

                if (multiplexor.Multiplexing.Role == DbcMultiplexingRole.Multiplexed)
                {
                    AddWarning(
                        "DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED",
                        $"Nested extended multiplexing for signal '{definition.SignalName}' through multiplexor '{definition.MultiplexorSignalName}' was skipped.",
                        definition.SourceLine);
                    continue;
                }

                if (multiplexor.Multiplexing.Role != DbcMultiplexingRole.Multiplexor)
                {
                    AddExtendedMultiplexingReferenceMissing(
                        $"Extended multiplexing signal '{definition.MultiplexorSignalName}' in message '{message.Name}' is not a multiplexor.",
                        definition.SourceLine);
                    continue;
                }

                if (signal.Multiplexing.Role == DbcMultiplexingRole.Multiplexor)
                {
                    AddWarning(
                        "DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED",
                        $"Extended multiplexing target '{definition.SignalName}' is itself a multiplexor and was skipped.",
                        definition.SourceLine);
                    continue;
                }

                if (signal.Multiplexing.Role == DbcMultiplexingRole.Multiplexed &&
                    signal.Multiplexing.MultiplexorSignalName is { Length: > 0 } existingMultiplexor &&
                    !string.Equals(existingMultiplexor, definition.MultiplexorSignalName, StringComparison.Ordinal))
                {
                    AddWarning(
                        "DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED",
                        $"Extended multiplexing target '{definition.SignalName}' references multiple multiplexors and was skipped.",
                        definition.SourceLine);
                    continue;
                }

                signal.Multiplexing = signal.Multiplexing.Role == DbcMultiplexingRole.None
                    ? DbcMultiplexing.Multiplexed(definition.MultiplexorSignalName, definition.Ranges)
                    : signal.Multiplexing.WithExtendedRanges(definition.MultiplexorSignalName, definition.Ranges);
            }
        }

        private static HashSet<string> GetDuplicateSignalNames(MessageBuilder message)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var signal in message.Signals)
            {
                counts.TryGetValue(signal.Name, out var count);
                counts[signal.Name] = count + 1;
            }

            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, count) in counts)
            {
                if (count > 1)
                {
                    duplicateNames.Add(name);
                }
            }

            return duplicateNames;
        }

        private bool TryFindUniqueSignal(MessageBuilder message, string signalName, int lineNumber, string metadataKind, out SignalBuilder signal)
        {
            signal = null!;
            var matchCount = 0;
            foreach (var candidate in message.Signals)
            {
                if (!string.Equals(candidate.Name, signalName, StringComparison.Ordinal))
                {
                    continue;
                }

                signal = candidate;
                matchCount++;
            }

            if (matchCount == 1)
            {
                return true;
            }

            if (matchCount > 1)
            {
                ReportAmbiguousSignalMetadata(message, signalName, metadataKind, lineNumber);
            }

            signal = null!;
            return false;
        }

        private static bool AnySignal(MessageBuilder message, string signalName)
        {
            foreach (var signal in message.Signals)
            {
                if (string.Equals(signal.Name, signalName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportAmbiguousSignalMetadata(MessageBuilder message, string signalName, string metadataKind, int lineNumber)
        {
            if (!ambiguousSignalMetadataDiagnostics.Add((message.RawId, signalName, metadataKind)))
            {
                return;
            }

            AddWarning(
                "DBC_SIGNAL_METADATA_AMBIGUOUS",
                $"Signal {metadataKind} for '{message.Name}.{signalName}' was not applied because the message contains multiple signals with that name.",
                lineNumber);
        }

        private static bool ValueDescriptionMapsEqual(
            IReadOnlyDictionary<long, string> left,
            IReadOnlyDictionary<long, string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var (key, value) in left)
            {
                if (!right.TryGetValue(key, out var otherValue) ||
                    !string.Equals(value, otherValue, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasMissingWhitespaceAfterQuotedAttributeName(string line)
        {
            var quoteStart = line.IndexOf('"');
            if (quoteStart < 0)
            {
                return false;
            }

            for (var i = quoteStart + 1; i < line.Length; i++)
            {
                if (line[i] != '"' || DbcQuotedText.IsEscapedQuote(line, i))
                {
                    continue;
                }

                return i + 3 < line.Length &&
                    !char.IsWhiteSpace(line[i + 1]) &&
                    IsAttributeOwnerToken(line.AsSpan(i + 1, Math.Min(3, line.Length - i - 1)));
            }

            return false;
        }

        private static bool IsAttributeOwnerToken(ReadOnlySpan<char> token)
        {
            return token.SequenceEqual("BO_") ||
                token.SequenceEqual("BU_") ||
                token.SequenceEqual("SG_") ||
                token.SequenceEqual("EV_");
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(' ', value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        }

        private void AddExtendedMultiplexingReferenceMissing(string message, int lineNumber)
        {
            AddRecoverableDiagnostic("DBC_EXTENDED_MULTIPLEXING_REFERENCE_MISSING", message, lineNumber);
        }

        private bool TryAddMalformedKnownKeywordDiagnostic(string line, int lineNumber)
        {
            if (line.StartsWith("BO_", StringComparison.Ordinal) &&
                !line.StartsWith("BO_TX_BU_", StringComparison.Ordinal))
            {
                currentMessage = null;
                AddRecoverableDiagnostic("DBC_MESSAGE_SYNTAX", $"Invalid message line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("SG_", StringComparison.Ordinal) &&
                !line.StartsWith("SG_MUL_VAL_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_SIGNAL_SYNTAX", $"Invalid signal line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("BA_DEF_DEF_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_ATTRIBUTE_DEFAULT_SYNTAX", $"Invalid attribute default line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("BA_DEF_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_ATTRIBUTE_DEFINITION_SYNTAX", $"Invalid attribute definition line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("BA_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_ATTRIBUTE_VALUE_SYNTAX", $"Invalid attribute value line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("VAL_TABLE_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_VALUE_TABLE_SYNTAX", $"Invalid value table line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("VAL_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_VALUE_DESCRIPTION_SYNTAX", $"Invalid value description line: {line}", lineNumber);
                return true;
            }

            if (line.StartsWith("SIG_VALTYPE_", StringComparison.Ordinal))
            {
                AddRecoverableDiagnostic("DBC_SIGNAL_VALUE_TYPE_SYNTAX", $"Invalid signal value type line: {line}", lineNumber);
                return true;
            }

            return false;
        }

        private NodeBuilder GetOrAddNode(string name)
        {
            if (!nodes.TryGetValue(name, out var node))
            {
                node = new NodeBuilder(name);
                nodes.Add(name, node);
            }

            return node;
        }

        private void AddRecoverableDiagnostic(string code, string message, int lineNumber)
        {
            if (options.Mode == DbcLoadMode.Strict)
            {
                AddError(code, message, lineNumber);
            }
            else
            {
                AddWarning(code, message, lineNumber);
            }
        }

        private void AddError(string code, string message, int lineNumber)
        {
            diagnostics.Add(new DbcDiagnostic(DbcDiagnosticSeverity.Error, code, message, lineNumber));
        }

        private void AddWarning(string code, string message, int lineNumber)
        {
            diagnostics.Add(new DbcDiagnostic(DbcDiagnosticSeverity.Warning, code, message, lineNumber));
        }

        private void AddAttributeReferenceMissing(string message, string attributeName, int lineNumber)
        {
            if (options.Mode == DbcLoadMode.Strict)
            {
                AddError("DBC_ATTRIBUTE_DEFINITION_MISSING", $"{message} '{attributeName}'.", lineNumber);
            }
            else
            {
                AddWarning("DBC_ATTRIBUTE_DEFINITION_MISSING", $"{message} '{attributeName}'.", lineNumber);
            }
        }

        private void AddMissingValueTable(string tableName, int lineNumber)
        {
            if (options.Mode == DbcLoadMode.Strict)
            {
                AddError("DBC_VALUE_TABLE_MISSING", $"Value table '{tableName}' was not found.", lineNumber);
            }
            else
            {
                AddWarning("DBC_VALUE_TABLE_MISSING", $"Value table '{tableName}' was not found.", lineNumber);
            }
        }

        private DbcSendType GetDefaultSendType(string attributeName)
        {
            return attributeDefinitions.TryGetValue(attributeName, out var definition) &&
                definition.DefaultValue is not null &&
                TryGetSendType(definition.DefaultValue, out var sendType)
                    ? sendType
                    : DbcSendType.Unknown;
        }

        private int? GetDefaultInt32(string attributeName)
        {
            return attributeDefinitions.TryGetValue(attributeName, out var definition) &&
                definition.DefaultValue is not null &&
                definition.DefaultValue.TryGetInt32(out var value)
                    ? value
                    : null;
        }

        private IEnumerable<DbcStatement> ReadStatements(string text)
        {
            var builder = new StringBuilder();
            var currentLine = 1;
            var statementLine = 1;
            var hasContent = false;
            var insideQuote = false;
            var discardingTooLongStatement = false;

            for (var i = 0; i < text.Length; i++)
            {
                var value = text[i];
                if (value == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    value = '\n';
                }

                if (discardingTooLongStatement)
                {
                    if (value == '\n')
                    {
                        currentLine++;
                        statementLine = currentLine;
                        discardingTooLongStatement = false;
                    }
                    else if (value == ';')
                    {
                        statementLine = currentLine;
                        discardingTooLongStatement = false;
                    }

                    continue;
                }

                if (!hasContent && !char.IsWhiteSpace(value))
                {
                    hasContent = true;
                    statementLine = currentLine;
                }

                if (value == '"' && !DbcQuotedText.IsEscapedQuote(text, i))
                {
                    insideQuote = !insideQuote;
                }

                if (value == ';' && !insideQuote)
                {
                    builder.Append(value);
                    if (builder.Length > options.MaxStatementLength)
                    {
                        AddStatementTooLong(statementLine);
                        builder.Clear();
                        hasContent = false;
                        insideQuote = false;
                        statementLine = currentLine;
                        continue;
                    }

                    var statement = builder.ToString().Trim();
                    if (statement.Length > 0)
                    {
                        yield return new DbcStatement(statement, statementLine, HasUnterminatedQuote: false);
                    }

                    builder.Clear();
                    hasContent = false;
                    statementLine = currentLine;
                    continue;
                }

                if (value == '\n')
                {
                    if (insideQuote)
                    {
                        builder.Append(value);
                        if (builder.Length > options.MaxStatementLength)
                        {
                            AddStatementTooLong(statementLine);
                            builder.Clear();
                            hasContent = false;
                            insideQuote = false;
                            discardingTooLongStatement = false;
                        }

                        currentLine++;
                        continue;
                    }

                    var statement = builder.ToString().Trim();
                    if (statement.Length > 0)
                    {
                        yield return new DbcStatement(statement, statementLine, HasUnterminatedQuote: false);
                    }

                    builder.Clear();
                    hasContent = false;
                    currentLine++;
                    statementLine = currentLine;
                    continue;
                }

                builder.Append(value);
                if (builder.Length > options.MaxStatementLength)
                {
                    AddStatementTooLong(statementLine);
                    builder.Clear();
                    hasContent = false;
                    insideQuote = false;
                    discardingTooLongStatement = true;
                }
            }

            var trailing = builder.ToString().Trim();
            if (trailing.Length > 0)
            {
                yield return new DbcStatement(trailing, statementLine, insideQuote);
            }
        }

        private void AddStatementTooLong(int lineNumber)
        {
            AddRecoverableDiagnostic(
                "DBC_STATEMENT_TOO_LONG",
                $"DBC statement exceeded the configured maximum length of {options.MaxStatementLength} characters and was skipped.",
                lineNumber);
        }

        private readonly record struct DbcStatement(string Text, int LineNumber, bool HasUnterminatedQuote);
    }

    private sealed class NodeBuilder(string name)
    {
        public string Name { get; } = name;

        public string? Comment { get; set; }

        public Dictionary<string, DbcAttributeValue> Attributes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MessageBuilder(uint rawId, string name, int dataLength, string transmitter, int sourceLine)
    {
        public uint RawId { get; } = rawId;

        public string Name { get; } = name;

        public int DataLength { get; } = dataLength;

        public string Transmitter { get; } = transmitter;

        public List<string> Transmitters { get; } = [transmitter];

        public int SourceLine { get; } = sourceLine;

        public string? Comment { get; set; }

        public int? CycleTimeMs { get; set; }

        public DbcSendType? SendType { get; set; }

        public int? TimeoutTimeMs { get; set; }

        public DbcFrameFlags FrameFlags { get; set; }

        public List<SignalBuilder> Signals { get; } = [];

        public Dictionary<string, DbcAttributeValue> Attributes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ExtendedMultiplexingBuilder(
        uint messageId,
        string signalName,
        string multiplexorSignalName,
        DbcMultiplexorRange[] ranges,
        int sourceLine)
    {
        public uint MessageId { get; } = messageId;

        public string SignalName { get; } = signalName;

        public string MultiplexorSignalName { get; } = multiplexorSignalName;

        public DbcMultiplexorRange[] Ranges { get; } = ranges;

        public int SourceLine { get; } = sourceLine;
    }

    private sealed class EnvironmentVariableBuilder(
        string name,
        int valueType,
        double minimum,
        double maximum,
        string unit,
        double initialValue,
        int identifier,
        string accessType,
        string[] accessNodes,
        int sourceLine)
    {
        public string Name { get; } = name;

        public int ValueType { get; } = valueType;

        public double Minimum { get; } = minimum;

        public double Maximum { get; } = maximum;

        public string Unit { get; } = unit;

        public double InitialValue { get; } = initialValue;

        public int Identifier { get; } = identifier;

        public string AccessType { get; } = accessType;

        public string[] AccessNodes { get; } = accessNodes;

        public int SourceLine { get; } = sourceLine;

        public Dictionary<string, DbcAttributeValue> Attributes { get; } = new(StringComparer.Ordinal);
    }

    private sealed record SignalCommentBuilder(string Text, int SourceLine);

    private sealed record SignalValueDescriptionBuilder(IReadOnlyDictionary<long, string> Values, int SourceLine);

    private sealed record SignalValueTypeBuilder(DbcSignalValueType ValueType, int SourceLine);

    private sealed record NameParts(string Name, string SourceName, IReadOnlyList<string> NameAliases);

    private sealed record PendingAttributeDefault(string AttributeName, string RawValue, int SourceLine);

    private sealed record PendingAttributeValue(
        string AttributeName,
        string RawValue,
        string Owner,
        string RawId,
        string SignalName,
        string NodeName,
        string EnvName,
        int SourceLine);

    private sealed class SignalBuilder(
        string name,
        int startBit,
        int bitLength,
        DbcByteOrder byteOrder,
        DbcSignalValueType valueType,
        double factor,
        double offset,
        double minimum,
        double maximum,
        string unit,
        string[] receivers,
        DbcMultiplexing multiplexing,
        int sourceLine)
    {
        public string Name { get; } = name;

        public int StartBit { get; } = startBit;

        public int BitLength { get; } = bitLength;

        public DbcByteOrder ByteOrder { get; } = byteOrder;

        public DbcSignalValueType ValueType { get; } = valueType;

        public double Factor { get; } = factor;

        public double Offset { get; } = offset;

        public double Minimum { get; } = minimum;

        public double Maximum { get; } = maximum;

        public string Unit { get; } = unit;

        public string[] Receivers { get; } = receivers;

        public DbcMultiplexing Multiplexing { get; set; } = multiplexing;

        public int SourceLine { get; } = sourceLine;

        public double? InitialValue { get; set; }

        public DbcSendType? SendType { get; set; }

        public int? TimeoutTimeMs { get; set; }

        public Dictionary<string, DbcAttributeValue> Attributes { get; } = new(StringComparer.Ordinal);
    }

    private static DbcMultiplexing ParseMultiplexing(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DbcMultiplexing.None;
        }

        if (string.Equals(value, "M", StringComparison.Ordinal))
        {
            return DbcMultiplexing.Multiplexor;
        }

        return value.StartsWith('m') && int.TryParse(value[1..].TrimEnd('M'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var switchValue)
            ? DbcMultiplexing.Multiplexed(switchValue)
            : DbcMultiplexing.None;
    }

    private static void ApplyMessageAttribute(string attributeName, DbcAttributeValue value, MessageBuilder message)
    {
        if (string.Equals(attributeName, "GenMsgCycleTime", StringComparison.Ordinal) && value.TryGetInt32(out var cycleTimeMs))
        {
            message.CycleTimeMs = cycleTimeMs;
            return;
        }

        if (string.Equals(attributeName, "GenMsgSendType", StringComparison.Ordinal))
        {
            TryGetSendType(value, out var sendType);
            message.SendType = sendType;
            return;
        }

        if (string.Equals(attributeName, "GenMsgTimeoutTime", StringComparison.Ordinal) && value.TryGetInt32(out var timeoutTimeMs))
        {
            message.TimeoutTimeMs = timeoutTimeMs;
            return;
        }

        if (string.Equals(attributeName, "VFrameFormat", StringComparison.Ordinal) && IsCanFdFrameFormat(value))
        {
            message.FrameFlags |= DbcFrameFlags.FlexibleDataRate;
        }
    }

    private static void ApplySignalAttribute(string attributeName, DbcAttributeValue value, SignalBuilder signal)
    {
        if (string.Equals(attributeName, "GenSigStartValue", StringComparison.Ordinal) && value.TryGetDouble(out var initialValue))
        {
            signal.InitialValue = initialValue;
            return;
        }

        if (string.Equals(attributeName, "GenSigSendType", StringComparison.Ordinal))
        {
            TryGetSendType(value, out var sendType);
            signal.SendType = sendType;
            return;
        }

        if (string.Equals(attributeName, "GenSigTimeoutTime", StringComparison.Ordinal) && value.TryGetInt32(out var timeoutTimeMs))
        {
            signal.TimeoutTimeMs = timeoutTimeMs;
        }
    }

    private static DbcAttributeOwnerKind ParseOwnerKind(string value)
    {
        return value switch
        {
            "BU_" => DbcAttributeOwnerKind.Node,
            "BO_" => DbcAttributeOwnerKind.Message,
            "SG_" => DbcAttributeOwnerKind.Signal,
            "EV_" => DbcAttributeOwnerKind.EnvironmentVariable,
            _ => DbcAttributeOwnerKind.Network,
        };
    }

    private static DbcAttributeValueKind ParseAttributeValueKind(string value)
    {
        return value switch
        {
            "INT" => DbcAttributeValueKind.Integer,
            "HEX" => DbcAttributeValueKind.Hex,
            "FLOAT" => DbcAttributeValueKind.Float,
            "STRING" => DbcAttributeValueKind.String,
            "ENUM" => DbcAttributeValueKind.Enum,
            _ => DbcAttributeValueKind.String,
        };
    }

    private static DbcAttributeValue CreateAttributeValue(DbcAttributeDefinition definition, string rawValue, int sourceLine)
    {
        var normalized = Unquote(rawValue);
        object? value = definition.ValueKind switch
        {
            DbcAttributeValueKind.Integer => ParseIntegerAttributeValue(normalized),
            DbcAttributeValueKind.Hex => CreateHexAttributeValue(definition, normalized),
            DbcAttributeValueKind.Float => ParseDouble(normalized),
            DbcAttributeValueKind.String => normalized,
            DbcAttributeValueKind.Enum => CreateEnumAttributeValue(definition, normalized),
            _ => normalized,
        };

        return new DbcAttributeValue(definition.Name, definition.ValueKind, normalized, value, sourceLine);
    }

    private static object CreateHexAttributeValue(DbcAttributeDefinition definition, string normalized)
    {
        try
        {
            return ParseHexAttributeValue(normalized);
        }
        catch (DbcNumericParseException) when (IsFractionalGenSigStartValue(definition, normalized, out var initialValue))
        {
            return initialValue;
        }
    }

    private static bool IsFractionalGenSigStartValue(DbcAttributeDefinition definition, string normalized, out double value)
    {
        if (!string.Equals(definition.Name, "GenSigStartValue", StringComparison.Ordinal) ||
            normalized.IndexOfAny(['e', 'E']) >= 0 ||
            normalized.IndexOf('.') < 0)
        {
            value = default;
            return false;
        }

        try
        {
            value = ParseDouble(normalized);
            return double.IsFinite(value);
        }
        catch (DbcNumericParseException)
        {
            value = default;
            return false;
        }
    }

    private static object CreateEnumAttributeValue(DbcAttributeDefinition definition, string normalized)
    {
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 &&
            index < definition.EnumValues.Count)
        {
            return definition.EnumValues[index];
        }

        return normalized;
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

    private static bool TryGetSendType(DbcAttributeValue value, out DbcSendType sendType)
    {
        var text = value.Value as string ?? value.RawValue;
        return TryParseSendType(text, out sendType);
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
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseQuotedValues(string text)
    {
        var values = new List<string>();
        foreach (Match match in QuotedValueRegex().Matches(text))
        {
            values.Add(DbcQuotedText.Unescape(match.Groups["text"].Value));
        }

        return values;
    }

    private static Dictionary<long, string> ParseValueDescriptionMap(string text)
    {
        var values = new Dictionary<long, string>();
        foreach (Match item in ValueDescriptionItemRegex().Matches(text))
        {
            values[ParseInt64(item.Groups["value"].Value)] = DbcQuotedText.Unescape(item.Groups["text"].Value);
        }

        return values;
    }

    private static bool TryParseMultiplexorRanges(string text, out DbcMultiplexorRange[] ranges)
    {
        var values = new List<DbcMultiplexorRange>();
        var consumed = 0;
        foreach (Match match in MultiplexorRangeRegex().Matches(text))
        {
            if (!ContainsOnlyRangeDelimiters(text.AsSpan(consumed, match.Index - consumed)) ||
                !long.TryParse(match.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum))
            {
                ranges = [];
                return false;
            }

            var maximumText = match.Groups["max"].Value;
            long maximum;
            if (maximumText.Length == 0)
            {
                maximum = minimum;
            }
            else if (!long.TryParse(maximumText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum))
            {
                ranges = [];
                return false;
            }

            if (maximum < minimum)
            {
                ranges = [];
                return false;
            }

            values.Add(new DbcMultiplexorRange(minimum, maximum));
            consumed = match.Index + match.Length;
        }

        if (values.Count == 0 || !ContainsOnlyRangeDelimiters(text.AsSpan(consumed)))
        {
            ranges = [];
            return false;
        }

        ranges = values.ToArray();
        return true;
    }

    private static bool ContainsOnlyRangeDelimiters(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character) && character != ',')
            {
                return false;
            }
        }

        return true;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? DbcQuotedText.Unescape(value[1..^1])
            : value;
    }

    private static double? TryParseOptionalDouble(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : ParseDouble(value);
    }

    private static object ParseIntegerAttributeValue(string value)
    {
        var text = value.Trim();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedInteger))
        {
            return NarrowSignedInteger(signedInteger);
        }

        try
        {
            var decimalValue = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (decimalValue != decimal.Truncate(decimalValue) ||
                decimalValue < long.MinValue ||
                decimalValue > long.MaxValue)
            {
                throw new FormatException();
            }

            return NarrowSignedInteger((long)decimalValue);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "integer attribute", ex);
        }
    }

    private static object ParseHexAttributeValue(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return NarrowUnsignedInteger(ulong.Parse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new DbcNumericParseException(value, "hex attribute", ex);
            }
        }

        try
        {
            if (text.StartsWith("-", StringComparison.Ordinal))
            {
                throw new FormatException();
            }

            if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedInteger))
            {
                return NarrowUnsignedInteger(unsignedInteger);
            }

            var decimalValue = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (decimalValue != decimal.Truncate(decimalValue) ||
                decimalValue < 0 ||
                decimalValue > ulong.MaxValue)
            {
                throw new FormatException();
            }

            return NarrowUnsignedInteger(decimal.ToUInt64(decimalValue));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "hex attribute", ex);
        }
    }

    private static object NarrowSignedInteger(long value)
    {
        if (value is >= int.MinValue and <= int.MaxValue)
        {
            return (int)value;
        }

        return value;
    }

    private static object NarrowUnsignedInteger(ulong value)
    {
        if (value <= int.MaxValue)
        {
            return (int)value;
        }

        if (value <= long.MaxValue)
        {
            return (long)value;
        }

        return value;
    }

    private static int ParseInt32(string value)
    {
        try
        {
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "Int32", ex);
        }
    }

    private static long ParseInt64(string value)
    {
        try
        {
            return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "Int64", ex);
        }
    }

    private static uint ParseUInt32(string value)
    {
        try
        {
            return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "UInt32", ex);
        }
    }

    private static double ParseDouble(string value)
    {
        try
        {
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DbcNumericParseException(value, "Double", ex);
        }
    }

    private sealed class DbcNumericParseException(string value, string targetType, Exception innerException)
        : Exception($"Could not parse '{value}' as {targetType}.", innerException);

    [GeneratedRegex(@"^BO_\s+(?<id>\d+)\s+(?<name>[A-Za-z_]\w*)\s*:\s*(?<length>\d+)\s+(?<tx>[A-Za-z_]\w*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageRegex();

    [GeneratedRegex(@"^NS_\s*:$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceListHeaderRegex();

    [GeneratedRegex(@"^SG_\s+(?<name>[A-Za-z_]\w*)\s*(?<mux>M|m\d+M?)?\s*:\s*(?<start>\d+)\|(?<length>\d+)@(?<order>[01])(?<sign>[+-])\s+\((?<factor>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?),(?<offset>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\)\s+\[(?<min>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\|(?<max>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\]\s+""(?<unit>(?:\\.|[^""\\])*)""\s+(?<rx>[A-Za-z_]\w*(?:[\s,]+[A-Za-z_]\w*)*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SignalRegex();

    [GeneratedRegex(@"^CM_\s+BO_\s+(?<id>\d+)\s+""(?<text>(?:\\.|[^""\\])*)""\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageCommentRegex();

    [GeneratedRegex(@"^CM_\s+""(?<text>(?:\\.|[^""\\])*)""\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentCommentRegex();

    [GeneratedRegex(@"^CM_\s+(?<id>\d+)\s+""(?<text>(?:\\.|[^""\\])*)""\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyMessageCommentRegex();

    [GeneratedRegex(@"^CM_\s+SG_\s+(?<id>\d+)\s+(?<signal>[A-Za-z_]\w*)\s+""(?<text>(?:\\.|[^""\\])*)""\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex SignalCommentRegex();

    [GeneratedRegex(@"^CM_\s+BU_\s+(?<node>[A-Za-z_]\w*)\s+""(?<text>(?:\\.|[^""\\])*)""\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeCommentRegex();

    [GeneratedRegex(@"^BA_DEF_(?:\s+(?<owner>BU_|BO_|SG_|EV_))?\s+""(?<name>(?:\\.|[^""\\])+)""\s+(?<kind>INT|HEX|FLOAT|STRING|ENUM)(?:\s+(?<min>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\s+(?<max>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?))?(?:\s+(?<enum>.*))?\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeDefinitionRegex();

    [GeneratedRegex(@"^BA_DEF_DEF_\s+""(?<name>(?:\\.|[^""\\])+)""\s+(?<value>""(?:\\.|[^""\\])*""|0[xX][0-9A-Fa-f]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_]\w*)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeDefaultRegex();

    [GeneratedRegex(@"^BA_\s+""(?<name>(?:\\.|[^""\\])+)""\s*(?:(?<owner>BU_)\s+(?<node>[A-Za-z_]\w*)|(?<owner>BO_)\s+(?<id>\d+)|(?<owner>SG_)\s+(?<id>\d+)\s+(?<signal>[A-Za-z_]\w*)|(?<owner>EV_)\s+(?<env>[A-Za-z_]\w*))?\s+(?<value>""(?:\\.|[^""\\])*""|0[xX][0-9A-Fa-f]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_]\w*)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeValueRegex();

    [GeneratedRegex(@"^VAL_\s+(?<id>\d+)\s+(?<signal>[A-Za-z_]\w*)\s+(?<values>.+)\s*;$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValueDescriptionRegex();

    [GeneratedRegex(@"^VAL_TABLE_\s+(?<name>[A-Za-z_]\w*)\s+(?<values>.+)\s*;$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValueTableRegex();

    [GeneratedRegex(@"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueTableReferenceRegex();

    [GeneratedRegex(@"(?<value>-?\d+)\s+""(?<text>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValueDescriptionItemRegex();

    [GeneratedRegex(@"^SIG_VALTYPE_\s+(?<id>\d+)\s+(?<signal>[A-Za-z_]\w*)\s*:\s*(?<type>[012])\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex SignalValueTypeRegex();

    [GeneratedRegex(@"^SG_MUL_VAL_\s+(?<id>\d+)\s+(?<signal>[A-Za-z_]\w*)\s+(?<multiplexor>[A-Za-z_]\w*)\s+(?<ranges>.+)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtendedMultiplexingRegex();

    [GeneratedRegex(@"^BO_TX_BU_\s+(?<id>\d+)\s*:\s*(?<nodes>[A-Za-z_]\w*(?:[\s,]+[A-Za-z_]\w*)*)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex AdditionalMessageTransmittersRegex();

    [GeneratedRegex(@"^EV_\s+(?<name>[A-Za-z_]\w*)\s*:\s*(?<type>-?\d+)\s+\[(?<min>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\|(?<max>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\]\s+""(?<unit>(?:\\.|[^""\\])*)""\s+(?<initial>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\s+(?<id>-?\d+)\s+(?<accessType>\S+)(?:\s+(?<nodes>[A-Za-z_]\w*(?:[\s,]+[A-Za-z_]\w*)*))?\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariableRegex();

    [GeneratedRegex(@"^BA_DEF_REL_\s+(?<relation>[A-Za-z_]\w*)\s+""(?<name>(?:\\.|[^""\\])+)""\s+(?<kind>INT|HEX|FLOAT|STRING|ENUM)(?:\s+(?<min>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\s+(?<max>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?))?(?:\s+(?<enum>.*))?\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationAttributeDefinitionRegex();

    [GeneratedRegex(@"^BA_DEF_DEF_REL_\s+""(?<name>(?:\\.|[^""\\])+)""\s+(?<value>""(?:\\.|[^""\\])*""|0[xX][0-9A-Fa-f]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_]\w*)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationAttributeDefaultRegex();

    [GeneratedRegex(@"^BA_REL_\s+""(?<name>(?:\\.|[^""\\])+)""\s+(?<target>.+?)\s+(?<value>""(?:\\.|[^""\\])*""|0[xX][0-9A-Fa-f]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_]\w*)\s*;$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationAttributeValueRegex();

    [GeneratedRegex(@"(?<min>\d+)\s*(?:-\s*(?<max>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex MultiplexorRangeRegex();

    [GeneratedRegex(@"""(?<text>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedValueRegex();
}
