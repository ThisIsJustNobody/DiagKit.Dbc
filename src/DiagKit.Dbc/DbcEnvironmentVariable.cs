using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// DBC 环境变量元数据（EV_），仅作为数据库元数据保留，不映射为 CAN frame signal。<br/>
/// DBC environment-variable metadata (EV_), preserved as database metadata and not mapped as CAN frame signals.
/// </summary>
public sealed class DbcEnvironmentVariable
{
    /// <summary>
    /// 创建环境变量元数据。<br/>
    /// Creates environment-variable metadata.
    /// </summary>
    public DbcEnvironmentVariable(
        string name,
        int valueType,
        double minimum,
        double maximum,
        string unit,
        double initialValue,
        int identifier,
        string accessType,
        IReadOnlyList<DbcNode>? accessNodes = null,
        int sourceLine = 0,
        IReadOnlyDictionary<string, DbcAttributeValue>? attributes = null,
        string? sourceName = null,
        IReadOnlyList<string>? nameAliases = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Environment variable name cannot be empty.", nameof(name))
            : name;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? Name : sourceName;
        NameAliases = DbcNameLookup.CreateAliases(Name, SourceName, nameAliases);
        ValueType = valueType;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        InitialValue = initialValue;
        Identifier = identifier;
        AccessType = accessType ?? throw new ArgumentNullException(nameof(accessType));
        AccessNodes = Array.AsReadOnly(accessNodes?.ToArray() ?? []);
        SourceLine = sourceLine;
        Attributes = attributes is null
            ? new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(attributes, StringComparer.Ordinal));
    }

    /// <summary>
    /// 环境变量名称 / Environment variable name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// DBC 源文件 EV_ 行中的原始环境变量名，可能是 Vector 32 字符截断名。<br/>
    /// Original environment-variable name from the DBC EV_ statement, possibly a Vector 32-character truncated name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// 可用于查找此环境变量的额外名称。<br/>
    /// Additional names that can resolve this environment variable.
    /// </summary>
    public IReadOnlyList<string> NameAliases { get; }

    /// <summary>
    /// EV_ 行中的值类型编码 / Value type code from the EV_ line.
    /// </summary>
    public int ValueType { get; }

    /// <summary>
    /// 最小物理值 / Minimum physical value.
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// 最大物理值 / Maximum physical value.
    /// </summary>
    public double Maximum { get; }

    /// <summary>
    /// 单位 / Unit.
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// 初始值 / Initial value.
    /// </summary>
    public double InitialValue { get; }

    /// <summary>
    /// EV_ 行中的环境变量 ID / Environment variable identifier from the EV_ line.
    /// </summary>
    public int Identifier { get; }

    /// <summary>
    /// 访问类型文本 / Access type text.
    /// </summary>
    public string AccessType { get; }

    /// <summary>
    /// 可访问节点 / Nodes allowed to access the environment variable.
    /// </summary>
    public IReadOnlyList<DbcNode> AccessNodes { get; }

    /// <summary>
    /// EV_ 语句所在行号 / Source line of the EV_ statement.
    /// </summary>
    public int SourceLine { get; }

    /// <summary>
    /// 环境变量级属性 / Environment-variable-level attributes.
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeValue> Attributes { get; }
}
