namespace DiagKit.Dbc.Workbook;

internal static class DbcWorkbookSchema
{
    internal const string NetworkSheet = "Network";
    internal const string NodesSheet = "Nodes";
    internal const string MessagesSheet = "Messages";
    internal const string SignalsSheet = "Signals";
    internal const string ValueDescriptionsSheet = "ValueDescriptions";
    internal const string MultiplexRangesSheet = "MultiplexRanges";
    internal const string EnvironmentVariablesSheet = "EnvironmentVariables";
    internal const string AttributeDefinitionsSheet = "AttributeDefinitions";
    internal const string AttributesSheet = "Attributes";
    internal const string RelationAttributeDefinitionsSheet = "RelationAttributeDefinitions";
    internal const string RelationAttributeDefaultsSheet = "RelationAttributeDefaults";
    internal const string RelationAttributesSheet = "RelationAttributes";

    internal static readonly string[] NetworkHeaders =
    [
        "comment",
    ];

    internal static readonly string[] MessageHeaders =
    [
        "message_name",
        "can_id",
        "id_format",
        "dlc",
        "is_can_fd",
        "transmitters",
        "cycle_time_ms",
        "send_type",
        "timeout_ms",
        "comment",
    ];

    internal static readonly string[] SignalHeaders =
    [
        "message_name",
        "signal_name",
        "start_bit",
        "length",
        "byte_order",
        "value_type",
        "factor",
        "offset",
        "minimum",
        "maximum",
        "unit",
        "receivers",
        "multiplex_role",
        "multiplex_switch_value",
        "multiplexor_signal_name",
        "initial_value",
        "send_type",
        "timeout_ms",
        "comment",
    ];

    internal static readonly string[] ValueDescriptionHeaders =
    [
        "message_name",
        "signal_name",
        "raw_value",
        "description",
    ];

    internal static readonly string[] MultiplexRangeHeaders =
    [
        "message_name",
        "signal_name",
        "multiplexor_signal_name",
        "range_minimum",
        "range_maximum",
    ];

    internal static readonly string[] EnvironmentVariableHeaders =
    [
        "environment_variable_name",
        "value_type",
        "minimum",
        "maximum",
        "unit",
        "initial_value",
        "identifier",
        "access_type",
        "access_nodes",
    ];

    internal static readonly string[] NodeHeaders =
    [
        "node_name",
        "comment",
    ];

    internal static readonly string[] AttributeDefinitionHeaders =
    [
        "owner_type",
        "attribute_name",
        "value_kind",
        "minimum",
        "maximum",
        "enum_values",
        "default_raw_value",
    ];

    internal static readonly string[] AttributeHeaders =
    [
        "owner_type",
        "message_name",
        "signal_name",
        "node_name",
        "environment_variable_name",
        "attribute_name",
        "raw_value",
    ];

    internal static readonly string[] RelationAttributeDefinitionHeaders =
    [
        "relation_kind",
        "attribute_name",
        "value_kind",
        "minimum",
        "maximum",
        "enum_values",
    ];

    internal static readonly string[] RelationAttributeDefaultHeaders =
    [
        "attribute_name",
        "raw_value",
    ];

    internal static readonly string[] RelationAttributeHeaders =
    [
        "attribute_name",
        "target",
        "raw_value",
    ];
}
