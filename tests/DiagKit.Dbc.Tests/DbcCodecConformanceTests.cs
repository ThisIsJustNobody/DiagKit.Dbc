namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcCodecConformanceTests
{
    [TestMethod]
    [DataRow(0, 1, 0x1UL)]
    [DataRow(7, 8, 0xA5UL)]
    [DataRow(4, 12, 0xABCUL)]
    [DataRow(13, 16, 0xBEEFUL)]
    [DataRow(1, 31, 0x1234_5678UL)]
    [DataRow(0, 32, 0x89AB_CDEFUL)]
    [DataRow(1, 63, 0x1234_5678_9ABC_DEFUL)]
    [DataRow(0, 64, 0xFEDC_BA98_7654_3210UL)]
    public void Intel_RoundTripsConformanceMatrix(int startBit, int bitLength, ulong value)
    {
        Span<byte> data = stackalloc byte[16];

        DbcBitCodec.Write(data, value, startBit, bitLength, DbcByteOrder.Intel);

        Assert.AreEqual(value, DbcBitCodec.Extract(data, startBit, bitLength, DbcByteOrder.Intel));
    }

    [TestMethod]
    [DataRow(7, 1, 0x1UL)]
    [DataRow(7, 8, 0xA5UL)]
    [DataRow(3, 12, 0xABCUL)]
    [DataRow(15, 16, 0xBEEFUL)]
    [DataRow(5, 31, 0x1234_5678UL)]
    [DataRow(31, 32, 0x89AB_CDEFUL)]
    [DataRow(6, 63, 0x1234_5678_9ABC_DEFUL)]
    [DataRow(7, 64, 0xFEDC_BA98_7654_3210UL)]
    public void Motorola_RoundTripsConformanceMatrix(int startBit, int bitLength, ulong value)
    {
        Span<byte> data = stackalloc byte[16];

        DbcBitCodec.Write(data, value, startBit, bitLength, DbcByteOrder.Motorola);

        Assert.AreEqual(value, DbcBitCodec.Extract(data, startBit, bitLength, DbcByteOrder.Motorola));
    }

    [TestMethod]
    public void DbcCodecFacade_ExtractsAndWritesRawAndPhysicalValues()
    {
        var signal = CreateSignal("Speed", 4, 12, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.5, -10, 0, 100);
        Span<byte> data = stackalloc byte[8];

        DbcCodec.WritePhysical(data, signal, 42.5);

        Assert.AreEqual(105UL, DbcCodec.ExtractRaw(data, signal));
        Assert.AreEqual(42.5, DbcCodec.DecodePhysical(data, signal), 0.000_001);
    }

    [TestMethod]
    public void CrossCheckGolden_EncodesMixedIntelAndMotorolaPhysicalSignalsIntoOnePayload()
    {
        var message = CreateMessage(
            [
                CreateSignal("Intel12", 4, 12, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.5, -10, -10, 2037.5),
                CreateSignal("Moto17", 23, 17, DbcByteOrder.Motorola, DbcSignalValueType.Unsigned, 1, 0, 0, 131071),
                CreateSignal("MotoSigned12", 55, 12, DbcByteOrder.Motorola, DbcSignalValueType.Signed, 0.25, -5, -517, 506.75),
            ]);
        Span<byte> data = stackalloc byte[8];
        ReadOnlySpan<byte> expected = [0x90, 0x06, 0xD5, 0xE6, 0x80, 0x00, 0xF7, 0xD0];

        Assert.IsTrue(message.TryEncodeSignal("Intel12", data, 42.5).Succeeded);
        Assert.IsTrue(message.TryEncodeSignal("Moto17", data, 0x1ABCD).Succeeded);
        Assert.IsTrue(message.TryEncodeSignal("MotoSigned12", data, -37.75).Succeeded);

        CollectionAssert.AreEqual(expected.ToArray(), data.ToArray());
        Assert.AreEqual(42.5, message.DecodeSignal("Intel12", data), 0.000_001);
        Assert.AreEqual(0x1ABCD, message.DecodeSignal("Moto17", data), 0.000_001);
        Assert.AreEqual(-37.75, message.DecodeSignal("MotoSigned12", data), 0.000_001);
    }

    [TestMethod]
    public void TryEncodePhysical_StrictFailsWhenPhysicalRangeIsExceededEvenIfRawRangeFits()
    {
        var signal = CreateSignal("Percent", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 100);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 120);

        Assert.AreEqual(SignalWriteStatus.OutOfPhysicalRange, result.Status);
        Assert.AreEqual(0UL, signal.DecodeRaw(data));
    }

    [TestMethod]
    public void TryEncodePhysical_CanClampToPhysicalRangeBeforeEncoding()
    {
        var signal = CreateSignal("Percent", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 100);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 120, SignalWritePolicy.ClampToPhysicalRange);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(100UL, result.RawValue);
        Assert.AreEqual(100, result.PhysicalValue);
        Assert.AreEqual(100, signal.DecodePhysical(data), 0.000_001);
    }

    [TestMethod]
    public void TryEncodePhysical_CanClampToRawRangeWithoutApplyingPhysicalLimits()
    {
        var signal = CreateSignal("Loose", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 100);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 300, SignalWritePolicy.ClampToRawRange);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(255UL, result.RawValue);
        Assert.AreEqual(255, signal.DecodePhysical(data), 0.000_001);
    }

    [TestMethod]
    public void SignalWriteResult_DefaultIsNotSuccess()
    {
        Assert.IsFalse(default(SignalWriteResult).Succeeded);
    }

    [TestMethod]
    public void WritePhysical_Unsigned64ClampDoesNotOverflow()
    {
        var signal = new DbcSignal("U64", 0, 64, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, double.PositiveInfinity, "", []);
        Span<byte> data = stackalloc byte[8];

        var result = DbcCodec.WritePhysical(data, signal, 1e30, SignalWritePolicy.ClampToRawRange);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ulong.MaxValue, result.RawValue);
    }

    [TestMethod]
    public void WritePhysical_Signed55UpperEdgeDoesNotFlipSign()
    {
        var max = (1L << 54) - 1;
        var signal = new DbcSignal("S55", 0, 55, DbcByteOrder.Intel, DbcSignalValueType.Signed, 1, 0, -max - 1, max, "", []);
        Span<byte> data = stackalloc byte[8];

        var result = DbcCodec.WritePhysical(data, signal, max, SignalWritePolicy.ClampToRawRange);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(max, signal.DecodePhysical(data), 0);
    }

    [TestMethod]
    public void TryEncodePhysical_StrictAcceptsSinglePointPhysicalRange()
    {
        var signal = CreateSignal("SinglePoint", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 42, 42);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 42);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(42UL, result.RawValue);
    }

    [TestMethod]
    public void TryEncodePhysical_StrictRejectsValuesOutsideSinglePointPhysicalRange()
    {
        var signal = CreateSignal("SinglePoint", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 42, 42);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 43);

        Assert.AreEqual(SignalWriteStatus.OutOfPhysicalRange, result.Status);
        Assert.AreEqual(0UL, signal.DecodeRaw(data));
    }

    [TestMethod]
    public void TryEncodePhysical_FactorIsZeroTakesPriorityOverPhysicalRange()
    {
        var signal = CreateSignal("ZeroFactor", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0, 0, 0, 10);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 12);

        Assert.AreEqual(SignalWriteStatus.FactorIsZero, result.Status);
        Assert.AreEqual(0UL, signal.DecodeRaw(data));
    }

    [TestMethod]
    public void TryEncodePhysical_ClampToPhysicalRangeDoesNotClampRawRange()
    {
        var signal = CreateSignal("WidePhysical", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 300);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, 500, SignalWritePolicy.ClampToPhysicalRange);

        Assert.AreEqual(SignalWriteStatus.OutOfRawRange, result.Status);
        Assert.AreEqual(0UL, signal.DecodeRaw(data));
    }

    [TestMethod]
    public void Signed64_RoundTripsMinimumValue()
    {
        var signal = CreateSignal("Signed64", 0, 64, DbcByteOrder.Intel, DbcSignalValueType.Signed, 1, 0, long.MinValue, long.MaxValue);
        Span<byte> data = stackalloc byte[8];

        var result = signal.TryEncodePhysical(data, long.MinValue);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(unchecked((ulong)long.MinValue), result.RawValue);
        Assert.AreEqual((double)long.MinValue, signal.DecodePhysical(data));
    }

    [TestMethod]
    public void FloatAndDoubleSignalsRoundTripIeeePayloads()
    {
        var singleSignal = CreateSignal("Single", 0, 32, DbcByteOrder.Intel, DbcSignalValueType.Float, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        var doubleSignal = CreateSignal("Double", 0, 64, DbcByteOrder.Intel, DbcSignalValueType.Double, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        Span<byte> singleData = stackalloc byte[8];
        Span<byte> doubleData = stackalloc byte[8];

        Assert.IsTrue(singleSignal.TryEncodePhysical(singleData, 12.25).Succeeded);
        Assert.IsTrue(doubleSignal.TryEncodePhysical(doubleData, -42.5).Succeeded);

        Assert.AreEqual(12.25, singleSignal.DecodePhysical(singleData), 0.000_001);
        Assert.AreEqual(-42.5, doubleSignal.DecodePhysical(doubleData), 0.000_001);
    }

    [TestMethod]
    public void NegativeFactor_RoundTripsAndClampsPhysicalRange()
    {
        var signal = CreateSignal("NegativeFactor", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, -0.1, 10, 0, 10);
        Span<byte> roundTripData = stackalloc byte[8];
        Span<byte> clampData = stackalloc byte[8];

        var encodeResult = signal.TryEncodePhysical(roundTripData, 7);
        var strictResult = signal.TryEncodePhysical(clampData, 12);
        var clampResult = signal.TryEncodePhysical(clampData, 12, SignalWritePolicy.ClampToPhysicalRange);

        Assert.IsTrue(encodeResult.Succeeded);
        Assert.AreEqual(30UL, encodeResult.RawValue);
        Assert.AreEqual(7, signal.DecodePhysical(roundTripData), 0.000_001);
        Assert.AreEqual(SignalWriteStatus.OutOfPhysicalRange, strictResult.Status);
        Assert.IsTrue(clampResult.Succeeded);
        Assert.AreEqual(0UL, clampResult.RawValue);
        Assert.AreEqual(10, clampResult.PhysicalValue, 0.000_001);
    }

    [TestMethod]
    public void Intel_ThrowsWhenRequestedRangeExceedsPayload()
    {
        var data = new byte[1];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DbcBitCodec.Extract(data, 4, 8, DbcByteOrder.Intel));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DbcBitCodec.Write(data, 0xFF, 4, 8, DbcByteOrder.Intel));
    }

    [TestMethod]
    public void WriteRaw_ThrowsWhenValueDoesNotFitSignalBitLength()
    {
        var signal = CreateSignal("ByteValue", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255);
        var data = new byte[8];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DbcCodec.WriteRaw(data, signal, 0x1FF));
    }

    [TestMethod]
    public void Motorola_ThrowsWhenRequestedRangeExceedsPayload()
    {
        var data = new byte[1];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DbcBitCodec.Extract(data, 3, 12, DbcByteOrder.Motorola));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DbcBitCodec.Write(data, 0xFFF, 3, 12, DbcByteOrder.Motorola));
    }

    private static DbcSignal CreateSignal(
        string name,
        int startBit,
        int bitLength,
        DbcByteOrder byteOrder,
        DbcSignalValueType valueType,
        double factor,
        double offset,
        double minimum,
        double maximum)
    {
        return new DbcSignal(
            name,
            startBit,
            bitLength,
            byteOrder,
            valueType,
            factor,
            offset,
            minimum,
            maximum,
            string.Empty,
            [new DbcNode("HOST")]);
    }

    private static DbcMessage CreateMessage(IReadOnlyList<DbcSignal> signals)
    {
        var ecu = new DbcNode("ECU");
        return new DbcMessage(new DbcRawMessageId(0x100), "Golden", 8, ecu, signals);
    }
}
