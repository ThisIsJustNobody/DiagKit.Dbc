using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// DBC 网络节点，记录名称、注释和属性。<br/>
/// DBC network node with name, comment, and attributes.
/// </summary>
public sealed class DbcNode
{
    /// <summary>
    /// 创建 DBC 节点。<br/>
    /// Creates a DBC node.
    /// </summary>
    public DbcNode(string name, string? comment = null, IReadOnlyDictionary<string, DbcAttributeValue>? attributes = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Node name cannot be empty.", nameof(name))
            : name;
        Comment = comment;
        Attributes = attributes is null
            ? new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(attributes, StringComparer.Ordinal));
    }

    /// <summary>
    /// 节点名称 / Node name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 节点注释 / Node comment.
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// 节点级属性 / Node-level attributes.
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeValue> Attributes { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Name;
    }
}
