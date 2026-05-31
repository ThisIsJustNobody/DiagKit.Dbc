namespace DiagKit.Dbc.Workbook.Tests;

internal static class WorkbookTestDocuments
{
    public static DbcDocument CreateEditableDocument()
    {
        var ecu = new DbcNode("ECU", "engine controller");
        var tester = new DbcNode("Tester");
        var status = new DbcSignal(
            "Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            0.1,
            0,
            0,
            250,
            "km/h",
            [tester],
            valueDescriptions: new Dictionary<long, string>
            {
                [0] = "Stopped",
                [1] = "Moving",
            },
            comment: "vehicle speed");

        return new DbcDocument(
            [ecu, tester],
            [
                new DbcMessage(
                    new DbcRawMessageId(0x100),
                    "VehicleStatus",
                    8,
                    ecu,
                    [status],
                    comment: "status message"),
            ]);
    }

    public static DbcDocument CreateTwoMessageDocument()
    {
        var ecu = new DbcNode("ECU");
        var tester = new DbcNode("Tester");
        return new DbcDocument(
            [ecu, tester],
            [
                new DbcMessage(
                    new DbcRawMessageId(0x100),
                    "VehicleStatus",
                    8,
                    ecu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.1, 0, 0, 250, "km/h", [tester])]),
                new DbcMessage(
                    new DbcRawMessageId(0x101),
                    "OtherStatus",
                    8,
                    ecu,
                    [new DbcSignal("Mode", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [tester])]),
            ]);
    }

    public static DbcDocument CreateAdvancedDocument()
    {
        var ecu = new DbcNode("ECU", "main ECU");
        var display = new DbcNode("Display");
        var mode = new DbcSignal(
            "Mode",
            0,
            4,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            15,
            "",
            [display],
            DbcMultiplexing.Multiplexor,
            valueDescriptions: new Dictionary<long, string> { [0] = "Off" });
        var speed = new DbcSignal(
            "Speed",
            8,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            0.1,
            0,
            0,
            250,
            "km/h",
            [display],
            DbcMultiplexing.Multiplexed(2).WithExtendedRanges("Mode", [new DbcMultiplexorRange(4, 6)]),
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeValueKind.Float, "1", 1d),
                ["GenSigSendType"] = new("GenSigSendType", DbcAttributeValueKind.Enum, "Event", "Event"),
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", DbcAttributeValueKind.Integer, "250", 250),
            },
            initialValue: 1,
            sendType: DbcSendType.Event,
            timeoutTimeMs: 250);
        var ignition = new DbcEnvironmentVariable(
            "Ignition",
            0,
            0,
            1,
            "bool",
            0,
            1,
            "DUMMY_NODE_VECTOR0",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["EnvKind"] = new("EnvKind", DbcAttributeValueKind.String, "Calibration", "Calibration"),
            });

        return new DbcDocument(
            [ecu, display],
            [
                new DbcMessage(
                    new DbcRawMessageId(512),
                    "MuxStatus",
                    8,
                    ecu,
                    [mode, speed],
                    attributes: new Dictionary<string, DbcAttributeValue>
                    {
                        ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "100", 100),
                    },
                    cycleTimeMs: 100),
            ],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["EnvKind"] = new("EnvKind", DbcAttributeOwnerKind.EnvironmentVariable, DbcAttributeValueKind.String),
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Float, minimum: 0, maximum: 65535),
                ["GenSigSendType"] = new(
                    "GenSigSendType",
                    DbcAttributeOwnerKind.Signal,
                    DbcAttributeValueKind.Enum,
                    ["NoSigSendType", "Cyclic", "Event"]),
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
            },
            comment: "network comment",
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [ignition.Name] = ignition,
            },
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", "BU_SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
            },
            relationAttributeDefaults: new Dictionary<string, DbcRelationAttributeDefault>
            {
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", "0"),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("GenSigTimeoutTime", "BU_SG_REL_ ECU 512 Speed", "100"),
            ]);
    }
}
