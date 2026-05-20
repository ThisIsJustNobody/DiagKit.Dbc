namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcDocumentTests
{
    [TestMethod]
    public void Document_ResolvesMessageSignalAndNodeRelationships()
    {
        var vcu = new DbcNode("VCU");
        var host = new DbcNode("HOST");
        var speed = new DbcSignal(
            "VehicleSpeed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            0.01,
            0,
            0,
            250,
            "km/h",
            [host]);
        var message = new DbcMessage(new DbcRawMessageId(0x100), "VehicleStatus", 8, vcu, [speed]);
        var document = new DbcDocument([vcu, host], [message]);

        Assert.AreSame(message, document.ResolveMessage("VehicleStatus"));
        Assert.AreSame(message, document.ResolveMessage(new CanIdentifier(0x100, CanIdFormat.Standard)));
        Assert.AreSame(speed, document.ResolveSignal("VehicleStatus", "VehicleSpeed"));
        CollectionAssert.AreEqual(new[] { message }, document.GetMessagesTransmittedBy("VCU").ToArray());
        CollectionAssert.AreEqual(new[] { message }, document.GetMessagesReceivedBy("HOST").ToArray());
        CollectionAssert.AreEqual(new[] { speed }, document.GetSignalsReceivedBy("HOST").ToArray());
    }

    [TestMethod]
    public void Document_TryResolveSignalReturnsFalseForMissingSignalOrMessage()
    {
        var vcu = new DbcNode("VCU");
        var host = new DbcNode("HOST");
        var speed = new DbcSignal("Speed", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host]);
        var message = new DbcMessage(new DbcRawMessageId(0x100), "Status", 8, vcu, [speed]);
        var document = new DbcDocument([vcu, host], [message]);

        Assert.IsTrue(document.TryResolveSignal("Status", "Speed", out var resolved));
        Assert.AreSame(speed, resolved);
        Assert.IsFalse(document.TryResolveSignal("Status", "Missing", out _));
        Assert.IsFalse(document.TryResolveSignal("Missing", "Speed", out _));
    }

    [TestMethod]
    public void Message_DlcFailsForUnsupportedTransportPayload()
    {
        var message = DbcLoader.LoadText("""
            VERSION ""
            BU_: ECU TESTER
            BO_ 2364539904 LargePG: 1785 ECU
             SG_ FirstByte : 0|8@1+ (1,0) [0|255] "" TESTER
            """, DbcLoadOptions.Lenient).GetDocumentOrThrow().ResolveMessage("LargePG");

        Assert.IsFalse(message.SupportsSingleFrameRuntime);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = message.Dlc);
    }

    [TestMethod]
    public void MessageAndCodecRejectUnsupportedTransportPayloadDecode()
    {
        var message = DbcLoader.LoadText("""
            VERSION ""
            BU_: ECU TESTER
            BO_ 2364539904 LargePG: 1785 ECU
             SG_ FirstByte : 0|8@1+ (1,0) [0|255] "" TESTER
            """, DbcLoadOptions.Lenient).GetDocumentOrThrow().ResolveMessage("LargePG");
        var signal = message.ResolveSignal("FirstByte");
        var payload = new byte[1785];
        var destination = new SignalSample[1];

        Assert.ThrowsExactly<InvalidOperationException>(() => message.DecodeSignal("FirstByte", payload));
        Assert.ThrowsExactly<InvalidOperationException>(() => DbcCodec.DecodePhysical(payload, signal));
        Assert.ThrowsExactly<InvalidOperationException>(() => DbcCodec.DecodeMessage(message, payload, destination));
    }

    [TestMethod]
    public void Constructors_CopyMutableInputCollections()
    {
        var ecu = new DbcNode("ECU");
        var host = new DbcNode("HOST");
        var receivers = new List<DbcNode> { host };
        var signalAttributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal)
        {
            ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", DbcAttributeValueKind.Integer, "100", 100),
        };
        var signal = new DbcSignal(
            "Speed",
            0,
            8,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            255,
            "",
            receivers,
            attributes: signalAttributes);

        var signals = new List<DbcSignal> { signal };
        var messageAttributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal)
        {
            ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "10", 10),
        };
        var message = new DbcMessage(
            new DbcRawMessageId(0x100),
            "Status",
            8,
            ecu,
            signals,
            attributes: messageAttributes);
        var nodes = new List<DbcNode> { ecu, host };
        var messages = new List<DbcMessage> { message };
        var documentAttributes = new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal)
        {
            ["Project"] = new("Project", DbcAttributeValueKind.String, "A", "A"),
        };

        var document = new DbcDocument(nodes, messages, attributes: documentAttributes);

        receivers.Add(new DbcNode("LATE_RX"));
        signalAttributes.Clear();
        signals.Add(new DbcSignal("LateSignal", 8, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host]));
        messageAttributes.Clear();
        nodes.Add(new DbcNode("LATE_NODE"));
        messages.Add(new DbcMessage(new DbcRawMessageId(0x101), "LateMessage", 8, ecu, []));
        documentAttributes.Clear();

        Assert.AreEqual(2, document.Nodes.Count);
        Assert.AreEqual(1, document.Messages.Count);
        Assert.AreEqual(1, message.Signals.Count);
        Assert.AreEqual(1, signal.Receivers.Count);
        Assert.AreEqual(1, signal.Attributes.Count);
        Assert.AreEqual(1, message.Attributes.Count);
        Assert.AreEqual(1, document.Attributes.Count);
        Assert.IsNotInstanceOfType(document.Nodes, typeof(DbcNode[]));
        Assert.IsNotInstanceOfType(message.Signals, typeof(DbcSignal[]));
        Assert.IsNotInstanceOfType(document.Attributes, typeof(Dictionary<string, DbcAttributeValue>));
        Assert.IsNotInstanceOfType(message.Attributes, typeof(Dictionary<string, DbcAttributeValue>));
        Assert.IsNotInstanceOfType(signal.Attributes, typeof(Dictionary<string, DbcAttributeValue>));
    }

    [TestMethod]
    public void OwningPayloadTypesExposeReadOnlySpanAndCopyInputData()
    {
        var framePayload = new byte[] { 1, 2, 3 };
        var snapshotPayload = new byte[] { 4, 5, 6 };
        var frame = new DbcFrame(new CanIdentifier(0x123, CanIdFormat.Standard), framePayload);
        var snapshot = new MessageSnapshot(
            new CanIdentifier(0x124, CanIdFormat.Standard),
            "Status",
            snapshotPayload,
            DbcFrameFlags.None,
            DbcTimestamp.Unspecified,
            SignalQuality.Valid);

        framePayload[0] = 9;
        snapshotPayload[0] = 9;

        Assert.AreEqual(typeof(ReadOnlySpan<byte>), typeof(DbcFrame).GetProperty(nameof(DbcFrame.Data))!.PropertyType);
        Assert.AreEqual(typeof(ReadOnlySpan<byte>), typeof(MessageSnapshot).GetProperty(nameof(MessageSnapshot.Data))!.PropertyType);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, frame.Data.ToArray());
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, snapshot.Data.ToArray());
    }

    [TestMethod]
    public void MalformedMultiplexedSignalDecodesAsInactive()
    {
        var ecu = new DbcNode("ECU");
        var host = new DbcNode("HOST");
        var mode = new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [host], DbcMultiplexing.Multiplexor);
        var malformed = new DbcSignal(
            "Malformed",
            8,
            8,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            255,
            "",
            [host],
            new DbcMultiplexing(DbcMultiplexingRole.Multiplexed, null));
        var message = new DbcMessage(new DbcRawMessageId(0x200), "Status", 8, ecu, [mode, malformed]);
        var samples = new SignalSample[message.Signals.Count];

        message.Decode(new byte[8], samples);

        Assert.AreEqual(SignalQuality.InactiveMultiplex, samples.Single(x => x.SignalName == "Malformed").Quality);
    }

    [TestMethod]
    public void SignalMessageBackReferenceIsAttachedOnlyOnceByMessageConstruction()
    {
        var ecu = new DbcNode("ECU");
        var host = new DbcNode("HOST");
        var signal = new DbcSignal("Speed", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host]);
        var first = new DbcMessage(new DbcRawMessageId(0x100), "First", 8, ecu, [signal]);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new DbcMessage(new DbcRawMessageId(0x101), "Second", 8, ecu, [signal]));

        Assert.AreSame(first, signal.Message);
        StringAssert.Contains(exception.Message, "already belongs");
        Assert.IsNull(typeof(DbcSignal).GetProperty(nameof(DbcSignal.Message))!.GetSetMethod(nonPublic: true));
    }

    [TestMethod]
    public void DuplicateNamedSignalsAreResolvableOnlyByEnumeration()
    {
        var ecu = new DbcNode("ECU");
        var host = new DbcNode("HOST");
        var first = new DbcSignal("Speed", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host]);
        var duplicate = new DbcSignal("Speed", 8, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host]);
        var message = new DbcMessage(new DbcRawMessageId(0x100), "Status", 8, ecu, [first, duplicate]);

        Assert.AreSame(message, first.Message);
        Assert.AreSame(message, duplicate.Message);
        Assert.AreEqual(2, message.FindSignals("Speed").Count);
        Assert.IsFalse(message.TryResolveSignal("Speed", out _));

        var exception = Assert.ThrowsExactly<DbcException>(() => message.ResolveSignal("Speed"));
        StringAssert.Contains(exception.Message, "ambiguous");
    }
}
