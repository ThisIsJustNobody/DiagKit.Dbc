namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcBitCodecTests
{
    [TestMethod]
    public void Intel_RoundTripsAcrossByteBoundary()
    {
        Span<byte> data = stackalloc byte[8];

        DbcBitCodec.Write(data, 0xABC, 4, 12, DbcByteOrder.Intel);

        Assert.AreEqual(0xABCul, DbcBitCodec.Extract(data, 4, 12, DbcByteOrder.Intel));
        Assert.AreEqual(0xC0, data[0]);
        Assert.AreEqual(0xAB, data[1]);
    }

    [TestMethod]
    public void Motorola_RoundTripsWholeBytesFromStartBitSeven()
    {
        Span<byte> data = stackalloc byte[8];

        DbcBitCodec.Write(data, 0x1234, 7, 16, DbcByteOrder.Motorola);

        Assert.AreEqual(0x1234ul, DbcBitCodec.Extract(data, 7, 16, DbcByteOrder.Motorola));
        Assert.AreEqual(0x12, data[0]);
        Assert.AreEqual(0x34, data[1]);
    }

    [TestMethod]
    public void Motorola_RoundTripsNibbleSplitSignal()
    {
        Span<byte> data = stackalloc byte[8];

        DbcBitCodec.Write(data, 0xAB, 3, 8, DbcByteOrder.Motorola);

        Assert.AreEqual(0xABul, DbcBitCodec.Extract(data, 3, 8, DbcByteOrder.Motorola));
        Assert.AreEqual(0x0A, data[0]);
        Assert.AreEqual(0xB0, data[1]);
    }

    [TestMethod]
    public void Motorola_WriteOutOfRangeLeavesPayloadUnchanged()
    {
        var data = new byte[] { 0xFF };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DbcBitCodec.Write(data, 0, 3, 8, DbcByteOrder.Motorola));

        CollectionAssert.AreEqual(new byte[] { 0xFF }, data);
    }

    [TestMethod]
    public void EmptyDataValidationUsesPublicParameterName()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => DbcBitCodec.Extract(ReadOnlySpan<byte>.Empty, 0, 1, DbcByteOrder.Intel));

        Assert.AreEqual("data", exception.ParamName);
    }
}
