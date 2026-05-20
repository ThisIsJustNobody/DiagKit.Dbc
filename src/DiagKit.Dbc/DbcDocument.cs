using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// DBC 加载后的文档模型，保存节点、消息、属性和值表等元数据。<br/>
/// DBC document model after loading, holding nodes, messages, attributes, and other metadata.
/// </summary>
public sealed class DbcDocument
{
    private static int nextRuntimeToken;
    private readonly Dictionary<string, DbcNode> nodesByName;
    private readonly Dictionary<string, DbcMessage> messagesByName;
    private readonly Dictionary<CanIdentifier, DbcMessage> messagesByIdentifier;

    /// <summary>
    /// 创建 DBC 文档实例。<br/>
    /// Creates a DBC document instance.
    /// </summary>
    public DbcDocument(
        IReadOnlyList<DbcNode> nodes,
        IReadOnlyList<DbcMessage> messages,
        IReadOnlyDictionary<string, DbcAttributeDefinition>? attributeDefinitions = null,
        IReadOnlyDictionary<string, DbcAttributeValue>? attributes = null,
        string? comment = null,
        IReadOnlyDictionary<string, DbcEnvironmentVariable>? environmentVariables = null,
        IReadOnlyDictionary<string, DbcRelationAttributeDefinition>? relationAttributeDefinitions = null,
        IReadOnlyDictionary<string, DbcRelationAttributeDefault>? relationAttributeDefaults = null,
        IReadOnlyList<DbcRelationAttributeValue>? relationAttributes = null)
    {
        Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
        Messages = Array.AsReadOnly((messages ?? throw new ArgumentNullException(nameof(messages))).ToArray());
        AttributeDefinitions = attributeDefinitions is null
            ? new ReadOnlyDictionary<string, DbcAttributeDefinition>(new Dictionary<string, DbcAttributeDefinition>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeDefinition>(new Dictionary<string, DbcAttributeDefinition>(attributeDefinitions, StringComparer.Ordinal));
        Attributes = attributes is null
            ? new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(attributes, StringComparer.Ordinal));
        Comment = comment;
        EnvironmentVariables = environmentVariables is null
            ? new ReadOnlyDictionary<string, DbcEnvironmentVariable>(new Dictionary<string, DbcEnvironmentVariable>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcEnvironmentVariable>(new Dictionary<string, DbcEnvironmentVariable>(environmentVariables, StringComparer.Ordinal));
        RelationAttributeDefinitions = relationAttributeDefinitions is null
            ? new ReadOnlyDictionary<string, DbcRelationAttributeDefinition>(new Dictionary<string, DbcRelationAttributeDefinition>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcRelationAttributeDefinition>(new Dictionary<string, DbcRelationAttributeDefinition>(relationAttributeDefinitions, StringComparer.Ordinal));
        RelationAttributeDefaults = relationAttributeDefaults is null
            ? new ReadOnlyDictionary<string, DbcRelationAttributeDefault>(new Dictionary<string, DbcRelationAttributeDefault>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcRelationAttributeDefault>(new Dictionary<string, DbcRelationAttributeDefault>(relationAttributeDefaults, StringComparer.Ordinal));
        RelationAttributes = Array.AsReadOnly(relationAttributes?.ToArray() ?? []);
        RuntimeToken = Interlocked.Increment(ref nextRuntimeToken);

        nodesByName = new Dictionary<string, DbcNode>(Nodes.Count, StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!nodesByName.TryAdd(node.Name, node))
            {
                throw new InvalidOperationException($"Duplicate node name '{node.Name}'.");
            }
        }

        messagesByName = new Dictionary<string, DbcMessage>(Messages.Count, StringComparer.Ordinal);
        messagesByIdentifier = new Dictionary<CanIdentifier, DbcMessage>(Messages.Count);
        foreach (var message in Messages)
        {
            if (!messagesByName.TryAdd(message.Name, message))
            {
                throw new InvalidOperationException($"Duplicate message name '{message.Name}'.");
            }

            if (!messagesByIdentifier.TryAdd(message.Identifier, message))
            {
                throw new InvalidOperationException($"Duplicate CAN identifier '{message.Identifier}'.");
            }
        }
    }

    /// <summary>
    /// DBC 节点列表 / List of DBC nodes.
    /// </summary>
    public IReadOnlyList<DbcNode> Nodes { get; }

    /// <summary>
    /// DBC 消息列表 / List of DBC messages.
    /// </summary>
    public IReadOnlyList<DbcMessage> Messages { get; }

    /// <summary>
    /// 属性定义字典 (BA_DEF_) / Attribute definition dictionary (BA_DEF_).
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeDefinition> AttributeDefinitions { get; }

    /// <summary>
    /// 网络级属性赋值 (BA_) / Network-level attribute value assignments (BA_).
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeValue> Attributes { get; }

    /// <summary>
    /// 文档级注释 (CM_) / Document-level comment (CM_).
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// 环境变量元数据 (EV_)，不作为 CAN frame signal 使用。<br/>
    /// Environment-variable metadata (EV_), not used as CAN frame signals.
    /// </summary>
    public IReadOnlyDictionary<string, DbcEnvironmentVariable> EnvironmentVariables { get; }

    /// <summary>
    /// 关系属性定义 (BA_DEF_REL_) 的原始元数据。<br/>
    /// Raw metadata for relation attribute definitions (BA_DEF_REL_).
    /// </summary>
    public IReadOnlyDictionary<string, DbcRelationAttributeDefinition> RelationAttributeDefinitions { get; }

    /// <summary>
    /// 关系属性默认值 (BA_DEF_DEF_REL_) 的原始元数据。<br/>
    /// Raw metadata for relation attribute defaults (BA_DEF_DEF_REL_).
    /// </summary>
    public IReadOnlyDictionary<string, DbcRelationAttributeDefault> RelationAttributeDefaults { get; }

    /// <summary>
    /// 关系属性赋值 (BA_REL_) 的原始元数据。<br/>
    /// Raw metadata for relation attribute assignments (BA_REL_).
    /// </summary>
    public IReadOnlyList<DbcRelationAttributeValue> RelationAttributes { get; }

    internal int RuntimeToken { get; }

    /// <summary>
    /// 按节点名查找 DBC 节点，名称匹配使用 ordinal 大小写敏感规则。<br/>
    /// Resolves a node by name using ordinal case-sensitive matching.
    /// </summary>
    public bool TryResolveNode(string nodeName, out DbcNode node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        return nodesByName.TryGetValue(nodeName, out node!);
    }

    /// <summary>
    /// 按节点名查找 DBC 节点，找不到时抛出 DbcException。<br/>
    /// Resolves a node by name, throws DbcException if not found.
    /// </summary>
    public DbcNode ResolveNode(string nodeName)
    {
        return TryResolveNode(nodeName, out var node)
            ? node
            : throw new DbcException($"Node '{nodeName}' was not found.");
    }

    /// <summary>
    /// 按消息名查找消息，名称匹配使用 ordinal 大小写敏感规则。<br/>
    /// Resolves a message by name using ordinal case-sensitive matching.
    /// </summary>
    public bool TryResolveMessage(string messageName, out DbcMessage message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        return messagesByName.TryGetValue(messageName, out message!);
    }

    /// <summary>
    /// 按 normalized CAN identifier 查找消息。<br/>
    /// Resolves a message by normalized CAN identifier.
    /// </summary>
    public bool TryResolveMessage(CanIdentifier identifier, out DbcMessage message)
    {
        return messagesByIdentifier.TryGetValue(identifier, out message!);
    }

    /// <summary>
    /// 按消息名查找消息，找不到时抛出 DbcException。<br/>
    /// Resolves a message by name, throws DbcException if not found.
    /// </summary>
    public DbcMessage ResolveMessage(string messageName)
    {
        return TryResolveMessage(messageName, out var message)
            ? message
            : throw new DbcException($"Message '{messageName}' was not found.");
    }

    /// <summary>
    /// 按 CAN identifier 查找消息，找不到时抛出 DbcException。<br/>
    /// Resolves a message by CAN identifier, throws DbcException if not found.
    /// </summary>
    public DbcMessage ResolveMessage(CanIdentifier identifier)
    {
        return TryResolveMessage(identifier, out var message)
            ? message
            : throw new DbcException($"Message '{identifier}' was not found.");
    }

    /// <summary>
    /// 按消息名和信号名查找 signal，找不到时返回 false。<br/>
    /// Resolves a signal by message name and signal name, returning false if not found.
    /// </summary>
    public bool TryResolveSignal(string messageName, string signalName, out DbcSignal signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        if (TryResolveMessage(messageName, out var message) &&
            message.TryResolveSignal(signalName, out signal))
        {
            return true;
        }

        signal = null!;
        return false;
    }

    /// <summary>
    /// 按 signal path 查找 signal，找不到或同名歧义时返回 false。<br/>
    /// Resolves a signal by signal path, returning false when missing or ambiguous.
    /// </summary>
    public bool TryResolveSignal(SignalPath signalPath, out DbcSignal signal)
    {
        return TryResolveSignal(signalPath.MessageName, signalPath.SignalName, out signal);
    }

    /// <summary>
    /// 按消息名和信号名查找 signal。<br/>
    /// Resolves a signal by message name and signal name.
    /// </summary>
    public DbcSignal ResolveSignal(string messageName, string signalName)
    {
        return TryResolveMessage(messageName, out var message)
            ? message.ResolveSignal(signalName)
            : throw new DbcException($"Message '{messageName}' was not found.");
    }

    /// <summary>
    /// 按 signal path 查找 signal。<br/>
    /// Resolves a signal by signal path.
    /// </summary>
    public DbcSignal ResolveSignal(SignalPath signalPath)
    {
        return ResolveSignal(signalPath.MessageName, signalPath.SignalName);
    }

    /// <summary>
    /// 获取指定节点发送的所有消息。<br/>
    /// Gets all messages transmitted by a given node.
    /// </summary>
    public IEnumerable<DbcMessage> GetMessagesTransmittedBy(string nodeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);

        foreach (var message in Messages)
        {
            foreach (var transmitter in message.Transmitters)
            {
                if (string.Equals(transmitter.Name, nodeName, StringComparison.Ordinal))
                {
                    yield return message;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 获取指定节点接收的所有消息。<br/>
    /// Gets all messages received by a given node.
    /// </summary>
    public IEnumerable<DbcMessage> GetMessagesReceivedBy(string nodeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);

        foreach (var message in Messages)
        {
            foreach (var signal in message.Signals)
            {
                if (ContainsNode(signal.Receivers, nodeName))
                {
                    yield return message;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 获取指定节点接收的所有信号。<br/>
    /// Gets all signals received by a given node.
    /// </summary>
    public IEnumerable<DbcSignal> GetSignalsReceivedBy(string nodeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);

        foreach (var message in Messages)
        {
            foreach (var signal in message.Signals)
            {
                if (ContainsNode(signal.Receivers, nodeName))
                {
                    yield return signal;
                }
            }
        }
    }

    private static bool ContainsNode(IReadOnlyList<DbcNode> nodes, string nodeName)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, nodeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
