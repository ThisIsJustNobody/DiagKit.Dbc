namespace DiagKit.Dbc;

/// <summary>
/// DBC 关系属性定义 (BA_DEF_REL_) 的原始元数据保留。<br/>
/// Preserved raw metadata for DBC relation attribute definitions (BA_DEF_REL_).
/// </summary>
public sealed class DbcRelationAttributeDefinition
{
    /// <summary>
    /// 创建关系属性定义。<br/>
    /// Creates a relation attribute definition.
    /// </summary>
    public DbcRelationAttributeDefinition(
        string name,
        string relationKind,
        DbcAttributeValueKind valueKind,
        IReadOnlyList<string>? enumValues = null,
        double? minimum = null,
        double? maximum = null,
        int sourceLine = 0)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Relation attribute name cannot be empty.", nameof(name))
            : name;
        RelationKind = string.IsNullOrWhiteSpace(relationKind)
            ? throw new ArgumentException("Relation kind cannot be empty.", nameof(relationKind))
            : relationKind;
        ValueKind = valueKind;
        EnumValues = Array.AsReadOnly(enumValues?.ToArray() ?? []);
        Minimum = minimum;
        Maximum = maximum;
        SourceLine = sourceLine;
    }

    /// <summary>
    /// 属性名称 / Attribute name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 关系目标类型文本，如 BU_SG_REL_。<br/>
    /// Relation target kind text, for example BU_SG_REL_.
    /// </summary>
    public string RelationKind { get; }

    /// <summary>
    /// 属性值类型 / Attribute value kind.
    /// </summary>
    public DbcAttributeValueKind ValueKind { get; }

    /// <summary>
    /// ENUM 类型的枚举值列表 / Enum values for ENUM kind.
    /// </summary>
    public IReadOnlyList<string> EnumValues { get; }

    /// <summary>
    /// 数值最小值 / Numeric minimum.
    /// </summary>
    public double? Minimum { get; }

    /// <summary>
    /// 数值最大值 / Numeric maximum.
    /// </summary>
    public double? Maximum { get; }

    /// <summary>
    /// BA_DEF_REL_ 语句所在行号 / Source line of the BA_DEF_REL_ statement.
    /// </summary>
    public int SourceLine { get; }
}

/// <summary>
/// DBC 关系属性默认值 (BA_DEF_DEF_REL_) 的原始元数据保留。<br/>
/// Preserved raw metadata for DBC relation attribute defaults (BA_DEF_DEF_REL_).
/// </summary>
public sealed class DbcRelationAttributeDefault
{
    /// <summary>
    /// 创建关系属性默认值。<br/>
    /// Creates a relation attribute default.
    /// </summary>
    public DbcRelationAttributeDefault(string name, string rawValue, int sourceLine = 0)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Relation attribute name cannot be empty.", nameof(name))
            : name;
        RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
        SourceLine = sourceLine;
    }

    /// <summary>
    /// 属性名称 / Attribute name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 原始默认值文本 / Raw default value text.
    /// </summary>
    public string RawValue { get; }

    /// <summary>
    /// BA_DEF_DEF_REL_ 语句所在行号 / Source line of the BA_DEF_DEF_REL_ statement.
    /// </summary>
    public int SourceLine { get; }
}

/// <summary>
/// DBC 关系属性赋值 (BA_REL_) 的原始元数据保留；当前不把该语句列入 CANdb++ known-good 导出。<br/>
/// Preserved raw metadata for DBC relation attribute assignments (BA_REL_); this statement is not currently in the CANdb++ known-good export set.
/// </summary>
public sealed class DbcRelationAttributeValue
{
    /// <summary>
    /// 创建关系属性赋值。<br/>
    /// Creates a relation attribute assignment.
    /// </summary>
    public DbcRelationAttributeValue(string name, string target, string rawValue, int sourceLine = 0)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Relation attribute name cannot be empty.", nameof(name))
            : name;
        Target = string.IsNullOrWhiteSpace(target)
            ? throw new ArgumentException("Relation target cannot be empty.", nameof(target))
            : target;
        RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
        SourceLine = sourceLine;
    }

    /// <summary>
    /// 属性名称 / Attribute name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 原始关系目标文本 / Raw relation target text.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// 原始赋值文本 / Raw assigned value text.
    /// </summary>
    public string RawValue { get; }

    /// <summary>
    /// BA_REL_ 语句所在行号 / Source line of the BA_REL_ statement.
    /// </summary>
    public int SourceLine { get; }
}
