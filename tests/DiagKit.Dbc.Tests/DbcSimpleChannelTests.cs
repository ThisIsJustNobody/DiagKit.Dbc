namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcSimpleChannelTests
{
    [TestMethod]
    public void SimpleChannel_WritesBuildsDecodesAndReadsPhysicalValuesByPath()
    {
        var document = LoadVehicleDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var simple = DbcSimpleChannel.Create(document);

        var write = simple.SetPhysicalValue("VehicleStatus.VehicleSpeed", 12.34, timestamp: Ms(1));
        var frame = simple.BuildFrame("VehicleStatus", Ms(2));
        var decoded = simple.Decode(new DbcFrameView(message.Identifier, frame.Data, timestamp: Ms(3)));

        Assert.IsTrue(write.Succeeded);
        Assert.AreEqual(message.Identifier, frame.Identifier);
        Assert.AreEqual(12.34, message.DecodeSignal("VehicleSpeed", frame.Data), 0.000_001);
        Assert.AreEqual("VehicleStatus", decoded.MessageName);
        Assert.AreEqual(1, decoded.Samples.Count);
        Assert.AreEqual(12.34, decoded.GetPhysicalValue("VehicleSpeed"), 0.000_001);
        Assert.IsTrue(decoded.TryGetPhysicalValue("VehicleSpeed", out var decodedValue));
        Assert.AreEqual(12.34, decodedValue, 0.000_001);
        Assert.IsTrue(simple.TryGetPhysicalValue("VehicleStatus.VehicleSpeed", out var currentValue));
        Assert.AreEqual(12.34, currentValue, 0.000_001);
        Assert.AreEqual(12.34, simple.GetPhysicalValue("VehicleStatus.VehicleSpeed"), 0.000_001);
    }

    [TestMethod]
    public void SimpleChannel_FailsClosedForAmbiguousSignalPath()
    {
        var document = DbcLoader.LoadText(
            """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 Track: 8 VCU
             SG_ CHECKSUM : 0|8@1+ (1,0) [0|255] "" HOST
             SG_ CHECKSUM : 8|8@1+ (1,0) [0|255] "" HOST
            """,
            DbcLoadOptions.Lenient).GetDocumentOrThrow();
        var simple = DbcSimpleChannel.Create(document);

        Assert.IsFalse(simple.TrySetPhysicalValue("Track.CHECKSUM", 1, out var result));
        Assert.IsFalse(result.Succeeded);
        var exception = Assert.ThrowsExactly<DbcException>(() => simple.SetPhysicalValue("Track.CHECKSUM", 1));
        StringAssert.Contains(exception.Message, "ambiguous");
    }

    [TestMethod]
    public void SimpleFrameValues_FailClosedForDuplicateSignalNames()
    {
        var samples = new[]
        {
            new SignalSample(new CanIdentifier(0x100, CanIdFormat.Standard), "Track", "CHECKSUM", Ms(1), 1, 1, SignalQuality.Valid),
            new SignalSample(new CanIdentifier(0x100, CanIdFormat.Standard), "Track", "CHECKSUM", Ms(1), 2, 2, SignalQuality.Valid),
        };
        var values = new DbcSimpleFrameValues("Track", samples);

        Assert.IsFalse(values.TryGetPhysicalValue("CHECKSUM", out _));
        var exception = Assert.ThrowsExactly<DbcException>(() => values.GetPhysicalValue("CHECKSUM"));
        StringAssert.Contains(exception.Message, "ambiguous");
    }

    private static DbcDocument LoadVehicleDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_ "GenMsgCycleTime" BO_ 256 10;
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcTimestamp Ms(int milliseconds)
    {
        return DbcTimestamp.FromElapsed(TimeSpan.FromMilliseconds(milliseconds));
    }
}
