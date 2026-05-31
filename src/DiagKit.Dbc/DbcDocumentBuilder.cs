namespace DiagKit.Dbc;

/// <summary>
/// DBC 文档语义 builder，用于新建或编辑后生成不可变 DbcDocument。<br/>
/// Semantic DBC document builder used to create or edit immutable DbcDocument instances.
/// </summary>
public sealed class DbcDocumentBuilder
{
    private readonly List<DbcNodeBuilder> nodes = [];
    private readonly List<DbcMessageBuilder> messages = [];
    private readonly Dictionary<string, DbcAttributeDefinition> attributeDefinitions;
    private readonly Dictionary<string, DbcAttributeValue> attributes;
    private readonly Dictionary<string, DbcEnvironmentVariable> environmentVariables;
    private readonly Dictionary<string, DbcRelationAttributeDefinition> relationAttributeDefinitions;
    private readonly Dictionary<string, DbcRelationAttributeDefault> relationAttributeDefaults;
    private readonly List<DbcRelationAttributeValue> relationAttributes;
    private readonly List<DbcNode> preservedNodeSources = [];
    private string? comment;

    private DbcDocumentBuilder()
    {
        attributeDefinitions = new Dictionary<string, DbcAttributeDefinition>(StringComparer.Ordinal);
        attributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
        environmentVariables = new Dictionary<string, DbcEnvironmentVariable>(StringComparer.Ordinal);
        relationAttributeDefinitions = new Dictionary<string, DbcRelationAttributeDefinition>(StringComparer.Ordinal);
        relationAttributeDefaults = new Dictionary<string, DbcRelationAttributeDefault>(StringComparer.Ordinal);
        relationAttributes = [];
    }

    /// <summary>
    /// 创建空 DBC 文档 builder。<br/>
    /// Creates an empty DBC document builder.
    /// </summary>
    public static DbcDocumentBuilder Create()
    {
        return new DbcDocumentBuilder();
    }

    /// <summary>
    /// 从现有 DBC 文档创建可编辑 builder。<br/>
    /// Creates an editable builder from an existing DBC document.
    /// </summary>
    public static DbcDocumentBuilder FromDocument(DbcDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = Create();
        builder.comment = document.Comment;
        CopyDictionary(document.AttributeDefinitions, builder.attributeDefinitions);
        CopyDictionary(document.Attributes, builder.attributes);
        CopyDictionary(document.EnvironmentVariables, builder.environmentVariables);
        CopyDictionary(document.RelationAttributeDefinitions, builder.relationAttributeDefinitions);
        CopyDictionary(document.RelationAttributeDefaults, builder.relationAttributeDefaults);
        builder.relationAttributes.AddRange(document.RelationAttributes);

        foreach (var node in document.Nodes)
        {
            builder.AddPreservedNode(node);
        }

        foreach (var message in document.Messages)
        {
            builder.AddPreservedNode(message.PrimaryTransmitter);
            foreach (var transmitter in message.Transmitters)
            {
                builder.AddPreservedNode(transmitter);
            }

            foreach (var signal in message.Signals)
            {
                foreach (var receiver in signal.Receivers)
                {
                    builder.AddPreservedNode(receiver);
                }
            }
        }

        foreach (var variable in document.EnvironmentVariables.Values)
        {
            foreach (var accessNode in variable.AccessNodes)
            {
                builder.AddPreservedNode(accessNode);
            }
        }

        foreach (var message in document.Messages)
        {
            builder.messages.Add(DbcMessageBuilder.FromMessage(message));
        }

        return builder;
    }

    private void AddPreservedNode(DbcNode node)
    {
        foreach (var preservedNode in preservedNodeSources)
        {
            if (ReferenceEquals(preservedNode, node))
            {
                return;
            }
        }

        foreach (var builder in nodes)
        {
            if (builder.SemanticallyEquals(node))
            {
                preservedNodeSources.Add(node);
                return;
            }
        }

        nodes.Add(DbcNodeBuilder.FromNode(node));
        preservedNodeSources.Add(node);
    }

    /// <summary>
    /// 添加 DBC 节点。<br/>
    /// Adds a DBC node.
    /// </summary>
    public DbcNodeBuilder AddNode(string name)
    {
        var node = new DbcNodeBuilder(name);
        nodes.Add(node);
        return node;
    }

    /// <summary>
    /// 添加 DBC message。<br/>
    /// Adds a DBC message.
    /// </summary>
    public DbcMessageBuilder AddMessage(DbcRawMessageId rawId, string name, int dataLength, string primaryTransmitterName)
    {
        var message = new DbcMessageBuilder(rawId, name, dataLength, primaryTransmitterName);
        messages.Add(message);
        return message;
    }

    /// <summary>
    /// 按名称取得唯一 message builder。<br/>
    /// Gets a unique message builder by name.
    /// </summary>
    public DbcMessageBuilder GetMessage(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DbcMessageBuilder? match = null;
        foreach (var message in messages)
        {
            if (!message.MatchesName(name))
            {
                continue;
            }

            if (match is not null)
            {
                throw new DbcException($"Message '{name}' is ambiguous.");
            }

            match = message;
        }

        return match ?? throw new DbcException($"Message '{name}' was not found.");
    }

    /// <summary>
    /// 生成不可变 DBC 文档。<br/>
    /// Builds an immutable DBC document.
    /// </summary>
    public DbcDocument Build()
    {
        EnsureReferencedNodes();

        var builtNodes = new List<DbcNode>(nodes.Count);
        foreach (var nodeBuilder in nodes)
        {
            builtNodes.Add(nodeBuilder.Build());
        }

        var nodeLookup = BuildNodeLookup(builtNodes);
        var builtMessages = new List<DbcMessage>(messages.Count);
        foreach (var messageBuilder in messages)
        {
            builtMessages.Add(messageBuilder.Build(nodeLookup));
        }

        return new DbcDocument(
            builtNodes,
            builtMessages,
            attributeDefinitions,
            attributes,
            comment,
            environmentVariables,
            relationAttributeDefinitions,
            relationAttributeDefaults,
            relationAttributes);
    }

    /// <summary>
    /// 构建文档并执行 writer validation。<br/>
    /// Builds the document and runs writer validation.
    /// </summary>
    public DbcValidationResult ValidateForWrite(DbcWriterOptions? options = null)
    {
        return DbcWriteValidator.Validate(Build(), options);
    }

    private void EnsureReferencedNodes()
    {
        foreach (var message in messages)
        {
            EnsureNode(message.PrimaryTransmitterName);
            foreach (var transmitterName in message.TransmitterNames)
            {
                EnsureNode(transmitterName);
            }

            foreach (var signal in message.Signals)
            {
                foreach (var receiverName in signal.ReceiverNames)
                {
                    EnsureNode(receiverName);
                }
            }
        }
    }

    private void EnsureNode(string name)
    {
        if (FindNodes(name).Count == 0)
        {
            nodes.Add(new DbcNodeBuilder(name));
        }
    }

    private List<DbcNodeBuilder> FindNodes(string name)
    {
        var matches = new List<DbcNodeBuilder>();
        foreach (var node in nodes)
        {
            if (MatchesName(node.Name, node.SourceName, node.NameAliases, name))
            {
                matches.Add(node);
            }
        }

        return matches;
    }

    internal static bool MatchesName(string name, string sourceName, IReadOnlyList<string> aliases, string candidate)
    {
        if (string.Equals(name, candidate, StringComparison.Ordinal) ||
            string.Equals(sourceName, candidate, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var alias in aliases)
        {
            if (string.Equals(alias, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static Dictionary<string, List<DbcNode>> BuildNodeLookup(IReadOnlyList<DbcNode> nodes)
    {
        var lookup = new Dictionary<string, List<DbcNode>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            AddNodeLookup(lookup, node.Name, node);
            AddNodeLookup(lookup, node.SourceName, node);
            foreach (var alias in node.NameAliases)
            {
                AddNodeLookup(lookup, alias, node);
            }
        }

        return lookup;
    }

    internal static DbcNode ResolveNode(IReadOnlyDictionary<string, List<DbcNode>> nodesByName, string name)
    {
        if (!nodesByName.TryGetValue(name, out var nodes) || nodes.Count == 0)
        {
            throw new DbcException($"Node '{name}' was not found.");
        }

        if (nodes.Count > 1)
        {
            throw new DbcException($"Node '{name}' is ambiguous.");
        }

        return nodes[0];
    }

    private static void AddNodeLookup(Dictionary<string, List<DbcNode>> lookup, string name, DbcNode node)
    {
        if (!lookup.TryGetValue(name, out var matches))
        {
            matches = [];
            lookup[name] = matches;
        }

        if (!matches.Contains(node))
        {
            matches.Add(node);
        }
    }

    private static void CopyDictionary<TValue>(
        IReadOnlyDictionary<string, TValue> source,
        Dictionary<string, TValue> destination)
    {
        foreach (var item in source)
        {
            destination[item.Key] = item.Value;
        }
    }
}

/// <summary>
/// DBC 节点 builder。<br/>
/// DBC node builder.
/// </summary>
public sealed class DbcNodeBuilder
{
    private readonly Dictionary<string, DbcAttributeValue> attributes;

    internal DbcNodeBuilder(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Node name cannot be empty.", nameof(name))
            : name;
        SourceName = Name;
        NameAliases = [];
        attributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
    }

    private DbcNodeBuilder(DbcNode node)
    {
        Name = node.Name;
        SourceName = node.SourceName;
        NameAliases = node.NameAliases.ToArray();
        Comment = node.Comment;
        attributes = new Dictionary<string, DbcAttributeValue>(node.Attributes, StringComparer.Ordinal);
    }

    /// <summary>
    /// 节点名称 / Node name.
    /// </summary>
    public string Name { get; }

    internal string SourceName { get; }

    internal IReadOnlyList<string> NameAliases { get; }

    internal string? Comment { get; }

    internal IReadOnlyDictionary<string, DbcAttributeValue> Attributes => attributes;

    internal static DbcNodeBuilder FromNode(DbcNode node)
    {
        return new DbcNodeBuilder(node);
    }

    internal DbcNode Build()
    {
        return new DbcNode(Name, Comment, attributes, SourceName, NameAliases);
    }

    internal bool SemanticallyEquals(DbcNode node)
    {
        return string.Equals(Name, node.Name, StringComparison.Ordinal) &&
            string.Equals(SourceName, node.SourceName, StringComparison.Ordinal) &&
            string.Equals(Comment, node.Comment, StringComparison.Ordinal) &&
            NameAliases.SequenceEqual(node.NameAliases, StringComparer.Ordinal) &&
            AttributesEqual(attributes, node.Attributes);
    }

    private static bool AttributesEqual(
        IReadOnlyDictionary<string, DbcAttributeValue> left,
        IReadOnlyDictionary<string, DbcAttributeValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var item in left)
        {
            if (!right.TryGetValue(item.Key, out var rightValue) ||
                !AttributeValueEquals(item.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AttributeValueEquals(DbcAttributeValue left, DbcAttributeValue right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            left.ValueKind == right.ValueKind &&
            string.Equals(left.RawValue, right.RawValue, StringComparison.Ordinal) &&
            Equals(left.Value, right.Value) &&
            left.SourceLine == right.SourceLine;
    }
}

/// <summary>
/// DBC message builder。<br/>
/// DBC message builder.
/// </summary>
public sealed class DbcMessageBuilder
{
    private readonly List<DbcSignalBuilder> signals = [];
    private readonly List<string> transmitterNames;
    private readonly Dictionary<string, DbcAttributeValue> attributes;
    private string? comment;

    internal DbcMessageBuilder(DbcRawMessageId rawId, string name, int dataLength, string primaryTransmitterName)
    {
        RawId = rawId;
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Message name cannot be empty.", nameof(name))
            : name;
        SourceName = Name;
        NameAliases = [];
        DataLength = dataLength;
        PrimaryTransmitterName = string.IsNullOrWhiteSpace(primaryTransmitterName)
            ? throw new ArgumentException("Primary transmitter name cannot be empty.", nameof(primaryTransmitterName))
            : primaryTransmitterName;
        transmitterNames = [PrimaryTransmitterName];
        attributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
    }

    private DbcMessageBuilder(DbcMessage message)
    {
        RawId = message.RawId;
        Name = message.Name;
        SourceName = message.SourceName;
        NameAliases = message.NameAliases.ToArray();
        DataLength = message.DataLength;
        PrimaryTransmitterName = message.PrimaryTransmitter.Name;
        transmitterNames = message.Transmitters.Select(x => x.Name).ToList();
        attributes = new Dictionary<string, DbcAttributeValue>(message.Attributes, StringComparer.Ordinal);
        comment = message.Comment;
        CycleTimeMs = message.CycleTimeMs;
        FrameFlags = message.FrameFlags;
        SourceLine = message.SourceLine;
        SendType = message.SendType;
        TimeoutTimeMs = message.TimeoutTimeMs;

        foreach (var signal in message.Signals)
        {
            signals.Add(DbcSignalBuilder.FromSignal(signal));
        }
    }

    internal DbcRawMessageId RawId { get; }

    internal string Name { get; }

    internal string SourceName { get; }

    internal IReadOnlyList<string> NameAliases { get; }

    internal int DataLength { get; }

    internal string PrimaryTransmitterName { get; }

    internal IReadOnlyList<string> TransmitterNames => transmitterNames;

    internal IReadOnlyList<DbcSignalBuilder> Signals => signals;

    internal int? CycleTimeMs { get; }

    internal DbcFrameFlags FrameFlags { get; }

    internal int SourceLine { get; }

    internal DbcSendType SendType { get; }

    internal int? TimeoutTimeMs { get; }

    internal static DbcMessageBuilder FromMessage(DbcMessage message)
    {
        return new DbcMessageBuilder(message);
    }

    /// <summary>
    /// 设置 message 注释。<br/>
    /// Sets the message comment.
    /// </summary>
    public DbcMessageBuilder WithComment(string? comment)
    {
        this.comment = comment;
        return this;
    }

    /// <summary>
    /// 添加或替换 message 属性。<br/>
    /// Adds or replaces a message attribute.
    /// </summary>
    public DbcMessageBuilder WithAttribute(DbcAttributeValue attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        attributes[attribute.Name] = attribute;
        return this;
    }

    /// <summary>
    /// 按名称取得当前 message 下的唯一 signal builder。<br/>
    /// Gets a unique signal builder by name within this message.
    /// </summary>
    public DbcSignalBuilder GetSignal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DbcSignalBuilder? match = null;
        foreach (var signal in signals)
        {
            if (!signal.MatchesName(name))
            {
                continue;
            }

            if (match is not null)
            {
                throw new DbcException($"Signal '{name}' is ambiguous in message '{Name}'.");
            }

            match = signal;
        }

        return match ?? throw new DbcException($"Signal '{name}' was not found in message '{Name}'.");
    }

    /// <summary>
    /// 添加 signal。<br/>
    /// Adds a signal.
    /// </summary>
    public DbcSignalBuilder AddSignal(string name, int startBit, int bitLength)
    {
        var signal = new DbcSignalBuilder(name, startBit, bitLength);
        signals.Add(signal);
        return signal;
    }

    internal bool MatchesName(string candidate)
    {
        return DbcDocumentBuilder.MatchesName(Name, SourceName, NameAliases, candidate);
    }

    internal DbcMessage Build(IReadOnlyDictionary<string, List<DbcNode>> nodesByName)
    {
        var primaryTransmitter = DbcDocumentBuilder.ResolveNode(nodesByName, PrimaryTransmitterName);
        var transmitters = transmitterNames.Select(name => DbcDocumentBuilder.ResolveNode(nodesByName, name)).ToArray();
        var builtSignals = signals.Select(signal => signal.Build(nodesByName)).ToArray();

        return new DbcMessage(
            RawId,
            Name,
            DataLength,
            primaryTransmitter,
            builtSignals,
            transmitters,
            attributes,
            comment,
            CycleTimeMs,
            FrameFlags,
            SourceLine,
            SendType,
            TimeoutTimeMs,
            SourceName,
            NameAliases);
    }
}

/// <summary>
/// DBC signal builder。<br/>
/// DBC signal builder.
/// </summary>
public sealed class DbcSignalBuilder
{
    private readonly List<string> receiverNames = [];
    private readonly Dictionary<long, string> valueDescriptions;
    private readonly Dictionary<string, DbcAttributeValue> attributes;
    private string? comment;

    internal DbcSignalBuilder(string name, int startBit, int bitLength)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Signal name cannot be empty.", nameof(name))
            : name;
        SourceName = Name;
        NameAliases = [];
        StartBit = startBit;
        BitLength = bitLength;
        ByteOrder = DbcByteOrder.Intel;
        ValueType = DbcSignalValueType.Unsigned;
        Factor = 1;
        Offset = 0;
        Minimum = 0;
        Maximum = 0;
        Unit = string.Empty;
        Multiplexing = DbcMultiplexing.None;
        valueDescriptions = new Dictionary<long, string>();
        attributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal);
    }

    private DbcSignalBuilder(DbcSignal signal)
    {
        Name = signal.Name;
        SourceName = signal.SourceName;
        NameAliases = signal.NameAliases.ToArray();
        StartBit = signal.StartBit;
        BitLength = signal.BitLength;
        ByteOrder = signal.ByteOrder;
        ValueType = signal.ValueType;
        Factor = signal.Factor;
        Offset = signal.Offset;
        Minimum = signal.Minimum;
        Maximum = signal.Maximum;
        Unit = signal.Unit;
        receiverNames = signal.Receivers.Select(x => x.Name).ToList();
        Multiplexing = signal.Multiplexing;
        valueDescriptions = new Dictionary<long, string>(signal.ValueDescriptions);
        attributes = new Dictionary<string, DbcAttributeValue>(signal.Attributes, StringComparer.Ordinal);
        comment = signal.Comment;
        InitialValue = signal.InitialValue;
        SourceLine = signal.SourceLine;
        SendType = signal.SendType;
        TimeoutTimeMs = signal.TimeoutTimeMs;
    }

    internal string Name { get; }

    internal string SourceName { get; }

    internal IReadOnlyList<string> NameAliases { get; }

    internal int StartBit { get; }

    internal int BitLength { get; }

    internal DbcByteOrder ByteOrder { get; private set; }

    internal DbcSignalValueType ValueType { get; private set; }

    internal double Factor { get; private set; }

    internal double Offset { get; private set; }

    internal double Minimum { get; private set; }

    internal double Maximum { get; private set; }

    internal string Unit { get; private set; }

    internal DbcMultiplexing Multiplexing { get; }

    internal IReadOnlyList<string> ReceiverNames => receiverNames;

    internal double? InitialValue { get; }

    internal int SourceLine { get; }

    internal DbcSendType SendType { get; }

    internal int? TimeoutTimeMs { get; }

    internal static DbcSignalBuilder FromSignal(DbcSignal signal)
    {
        return new DbcSignalBuilder(signal);
    }

    /// <summary>
    /// 设置信号字节序。<br/>
    /// Sets the signal byte order.
    /// </summary>
    public DbcSignalBuilder WithByteOrder(DbcByteOrder byteOrder)
    {
        ByteOrder = byteOrder;
        return this;
    }

    /// <summary>
    /// 设置信号值类型。<br/>
    /// Sets the signal value type.
    /// </summary>
    public DbcSignalBuilder WithValueType(DbcSignalValueType valueType)
    {
        ValueType = valueType;
        return this;
    }

    /// <summary>
    /// 设置信号缩放参数。<br/>
    /// Sets signal scaling.
    /// </summary>
    public DbcSignalBuilder WithScaling(double factor, double offset)
    {
        Factor = factor;
        Offset = offset;
        return this;
    }

    /// <summary>
    /// 设置信号物理范围。<br/>
    /// Sets the signal physical range.
    /// </summary>
    public DbcSignalBuilder WithRange(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        return this;
    }

    /// <summary>
    /// 设置信号单位。<br/>
    /// Sets the signal unit.
    /// </summary>
    public DbcSignalBuilder WithUnit(string? unit)
    {
        Unit = unit ?? string.Empty;
        return this;
    }

    /// <summary>
    /// 添加接收节点名称。<br/>
    /// Adds a receiver node name.
    /// </summary>
    public DbcSignalBuilder WithReceiver(string receiverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiverName);
        receiverNames.Add(receiverName);
        return this;
    }

    /// <summary>
    /// 设置信号注释。<br/>
    /// Sets the signal comment.
    /// </summary>
    public DbcSignalBuilder WithComment(string? comment)
    {
        this.comment = comment;
        return this;
    }

    /// <summary>
    /// 添加或替换 raw value 描述。<br/>
    /// Adds or replaces a raw value description.
    /// </summary>
    public DbcSignalBuilder WithValueDescription(long rawValue, string description)
    {
        valueDescriptions[rawValue] = description ?? throw new ArgumentNullException(nameof(description));
        return this;
    }

    /// <summary>
    /// 添加或替换 signal 属性。<br/>
    /// Adds or replaces a signal attribute.
    /// </summary>
    public DbcSignalBuilder WithAttribute(DbcAttributeValue attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        attributes[attribute.Name] = attribute;
        return this;
    }

    internal DbcSignal Build(IReadOnlyDictionary<string, List<DbcNode>> nodesByName)
    {
        var receivers = receiverNames.Select(name => DbcDocumentBuilder.ResolveNode(nodesByName, name)).ToArray();
        return new DbcSignal(
            Name,
            StartBit,
            BitLength,
            ByteOrder,
            ValueType,
            Factor,
            Offset,
            Minimum,
            Maximum,
            Unit,
            receivers,
            Multiplexing,
            valueDescriptions,
            attributes,
            comment,
            InitialValue,
            SourceLine,
            SendType,
            TimeoutTimeMs,
            SourceName,
            NameAliases);
    }

    internal bool MatchesName(string candidate)
    {
        return DbcDocumentBuilder.MatchesName(Name, SourceName, NameAliases, candidate);
    }
}
