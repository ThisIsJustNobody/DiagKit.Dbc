namespace DiagKit.Dbc;

/// <summary>
/// DBC 属性所有者的对象类型 / DBC attribute owner object kind.
/// </summary>
public enum DbcAttributeOwnerKind
{
    /// <summary>
    /// 网络级 / Network-level.
    /// </summary>
    Network,

    /// <summary>
    /// 节点级 / Node-level.
    /// </summary>
    Node,

    /// <summary>
    /// 消息级 / Message-level.
    /// </summary>
    Message,

    /// <summary>
    /// 信号级 / Signal-level.
    /// </summary>
    Signal,

    /// <summary>
    /// 环境变量级 / Environment-variable-level.
    /// </summary>
    EnvironmentVariable,
}

/// <summary>
/// DBC 属性值的类型 / DBC attribute value kind.
/// </summary>
public enum DbcAttributeValueKind
{
    /// <summary>
    /// 有符号整数 / Signed integer.
    /// </summary>
    Integer,

    /// <summary>
    /// 十六进制整数 / Hexadecimal integer.
    /// </summary>
    Hex,

    /// <summary>
    /// 浮点数 / Float.
    /// </summary>
    Float,

    /// <summary>
    /// 字符串 / String.
    /// </summary>
    String,

    /// <summary>
    /// 枚举 / Enum.
    /// </summary>
    Enum,
}

/// <summary>
/// DBC 属性定义 (BA_DEF_)，描述属性的名称、类型、范围和约束。<br/>
/// DBC attribute definition (BA_DEF_), describing the attribute name, type, range, and constraints.
/// </summary>
public sealed class DbcAttributeDefinition
{
    /// <summary>
    /// 创建属性定义。<br/>
    /// Creates an attribute definition.
    /// </summary>
    public DbcAttributeDefinition(
        string name,
        DbcAttributeOwnerKind ownerKind,
        DbcAttributeValueKind valueKind,
        IReadOnlyList<string>? enumValues = null,
        double? minimum = null,
        double? maximum = null,
        DbcAttributeValue? defaultValue = null,
        int sourceLine = 0)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Attribute name cannot be empty.", nameof(name))
            : name;
        OwnerKind = ownerKind;
        ValueKind = valueKind;
        EnumValues = enumValues is null
            ? Array.AsReadOnly(Array.Empty<string>())
            : Array.AsReadOnly(enumValues.ToArray());
        Minimum = minimum;
        Maximum = maximum;
        DefaultValue = defaultValue;
        SourceLine = sourceLine;
    }

    /// <summary>
    /// 属性名称 / Attribute name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 属性的所有者类型 / Owner kind of the attribute.
    /// </summary>
    public DbcAttributeOwnerKind OwnerKind { get; }

    /// <summary>
    /// 属性值类型 / Value kind of the attribute.
    /// </summary>
    public DbcAttributeValueKind ValueKind { get; }

    /// <summary>
    /// ENUM 类型的枚举值列表 / Enum value list for ENUM type.
    /// </summary>
    public IReadOnlyList<string> EnumValues { get; }

    /// <summary>
    /// INT/HEX/FLOAT 类型的最小值 / Minimum value for INT/HEX/FLOAT types.
    /// </summary>
    public double? Minimum { get; }

    /// <summary>
    /// INT/HEX/FLOAT 类型的最大值 / Maximum value for INT/HEX/FLOAT types.
    /// </summary>
    public double? Maximum { get; }

    /// <summary>
    /// 默认值 (BA_DEF_DEF_)，由 Loader 填充。<br/>
    /// Default value (BA_DEF_DEF_), set by the Loader.
    /// </summary>
    public DbcAttributeValue? DefaultValue { get; internal set; }

    /// <summary>
    /// BA_DEF_ 语句所在行号 / Source line of the BA_DEF_ statement.
    /// </summary>
    public int SourceLine { get; }
}

/// <summary>
/// DBC 属性赋值 (BA_)，将属性名绑定到具体值，关联到某个对象或网络。<br/>
/// DBC attribute value assignment (BA_), binding an attribute name to a concrete value scoped to an object or network.
/// </summary>
public sealed class DbcAttributeValue
{
    /// <summary>
    /// 创建属性赋值。<br/>
    /// Creates an attribute value assignment.
    /// </summary>
    public DbcAttributeValue(string name, DbcAttributeValueKind valueKind, string rawValue, object? value, int sourceLine = 0)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Attribute name cannot be empty.", nameof(name))
            : name;
        ValueKind = valueKind;
        RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
        Value = value;
        SourceLine = sourceLine;
    }

    /// <summary>
    /// 属性名称 / Attribute name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 属性值类型 / Value kind.
    /// </summary>
    public DbcAttributeValueKind ValueKind { get; }

    /// <summary>
    /// DBC 文件中的原始值文本 / Raw value text from the DBC file.
    /// </summary>
    public string RawValue { get; }

    /// <summary>
    /// 已解析的值 (int, double, string 等) / Parsed value (int, double, string, etc.).
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// BA_ 语句所在行号 / Source line of the BA_ statement.
    /// </summary>
    public int SourceLine { get; }

    /// <summary>
    /// 尝试将属性值作为 int 读取。<br/>
    /// Tries to read the attribute value as int.
    /// </summary>
    public bool TryGetInt32(out int value)
    {
        switch (Value)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case ulong ulongValue when ulongValue <= int.MaxValue:
                value = (int)ulongValue;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// 尝试将属性值作为 long 读取。<br/>
    /// Tries to read the attribute value as long.
    /// </summary>
    public bool TryGetInt64(out long value)
    {
        switch (Value)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                value = (long)ulongValue;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// 尝试将属性值作为 ulong 读取。<br/>
    /// Tries to read the attribute value as ulong.
    /// </summary>
    public bool TryGetUInt64(out ulong value)
    {
        switch (Value)
        {
            case int intValue when intValue >= 0:
                value = (ulong)intValue;
                return true;
            case long longValue when longValue >= 0:
                value = (ulong)longValue;
                return true;
            case ulong ulongValue:
                value = ulongValue;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// 尝试将属性值作为 double 读取。<br/>
    /// Tries to read the attribute value as double.
    /// </summary>
    public bool TryGetDouble(out double value)
    {
        switch (Value)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case ulong ulongValue:
                value = ulongValue;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
