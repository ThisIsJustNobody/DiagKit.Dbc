using System.Runtime.CompilerServices;

namespace DiagKit.Dbc;

/// <summary>
/// DBC 信号 bit 级编解码器，支持 Intel (LSB first) 和 Motorola (MSB first) 两种字节序的 extract/write 操作。<br/>
/// DBC signal bit-level codec supporting extract/write for Intel (LSB first) and Motorola (MSB first) byte orders.
/// </summary>
public static class DbcBitCodec
{
    /// <summary>
    /// 按字节序从 payload 中提取指定 bit 区间的 raw value。<br/>
    /// Extracts a raw value from the specified bit range within payload using the given byte order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Extract(ReadOnlySpan<byte> data, int startBit, int bitLength, DbcByteOrder byteOrder)
    {
        return byteOrder switch
        {
            DbcByteOrder.Intel => ExtractIntel(data, startBit, bitLength),
            DbcByteOrder.Motorola => ExtractMotorola(data, startBit, bitLength),
            _ => throw new ArgumentOutOfRangeException(nameof(byteOrder), byteOrder, "Unsupported byte order."),
        };
    }

    /// <summary>
    /// 按字节序将 raw value 写入 payload 中指定 bit 区间。<br/>
    /// Writes a raw value into the specified bit range within payload using the given byte order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(Span<byte> data, ulong value, int startBit, int bitLength, DbcByteOrder byteOrder)
    {
        switch (byteOrder)
        {
            case DbcByteOrder.Intel:
                WriteIntel(data, value, startBit, bitLength);
                return;
            case DbcByteOrder.Motorola:
                WriteMotorola(data, value, startBit, bitLength);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(byteOrder), byteOrder, "Unsupported byte order.");
        }
    }

    /// <summary>
    /// Intel (LSB first) 字节序提取 raw value。<br/>
    /// Extracts raw value using Intel (LSB first) byte order.
    /// </summary>
    public static ulong ExtractIntel(ReadOnlySpan<byte> data, int startBit, int bitLength)
    {
        ValidateCommon(data, startBit, bitLength);

        var byteIndex = startBit / 8;
        var bitOffset = startBit % 8;
        var bytesToRead = (bitOffset + bitLength + 7) / 8;
        if (byteIndex + bytesToRead > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Not enough data for the requested bit range.");
        }

        UInt128 buffer = 0;
        var source = data.Slice(byteIndex, bytesToRead);
        for (var i = 0; i < source.Length; i++)
        {
            buffer |= (UInt128)source[i] << (i * 8);
        }

        buffer >>= bitOffset;
        return (ulong)(buffer & Mask128(bitLength));
    }

    /// <summary>
    /// Intel (LSB first) 字节序写入 raw value。<br/>
    /// Writes raw value using Intel (LSB first) byte order.
    /// </summary>
    public static void WriteIntel(Span<byte> data, ulong value, int startBit, int bitLength)
    {
        ValidateCommon(data, startBit, bitLength);

        var byteIndex = startBit / 8;
        var bitOffset = startBit % 8;
        var bytesCount = (bitOffset + bitLength + 7) / 8;
        if (byteIndex + bytesCount > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Target span is too small for the requested bit range.");
        }

        UInt128 buffer = 0;
        var target = data.Slice(byteIndex, bytesCount);
        for (var i = 0; i < target.Length; i++)
        {
            buffer |= (UInt128)target[i] << (i * 8);
        }

        var valueMask = Mask128(bitLength);
        var shiftedMask = valueMask << bitOffset;
        buffer &= ~shiftedMask;
        buffer |= ((UInt128)value & valueMask) << bitOffset;

        for (var i = 0; i < target.Length; i++)
        {
            target[i] = (byte)(buffer >> (i * 8));
        }
    }

    /// <summary>
    /// Motorola (MSB first) 字节序提取 raw value。<br/>
    /// Extracts raw value using Motorola (MSB first) byte order.
    /// </summary>
    public static ulong ExtractMotorola(ReadOnlySpan<byte> data, int startBit, int bitLength)
    {
        ValidateCommon(data, startBit, bitLength);

        var byteIndex = startBit / 8;
        var bitInByte = startBit % 8;
        ulong result = 0;

        for (var i = 0; i < bitLength; i++)
        {
            if ((uint)byteIndex >= (uint)data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(data), "Not enough data for the requested Motorola bit range.");
            }

            result = (result << 1) | (uint)((data[byteIndex] >> bitInByte) & 1);
            MoveMotorolaCursor(ref byteIndex, ref bitInByte);
        }

        return result;
    }

    /// <summary>
    /// Motorola (MSB first) 字节序写入 raw value。<br/>
    /// Writes raw value using Motorola (MSB first) byte order.
    /// </summary>
    public static void WriteMotorola(Span<byte> data, ulong value, int startBit, int bitLength)
    {
        ValidateCommon(data, startBit, bitLength);

        var byteIndex = startBit / 8;
        var bitInByte = startBit % 8;
        value &= (ulong)Mask128(bitLength);

        ValidateMotorolaRange(data, byteIndex, bitInByte, bitLength);

        for (var i = 0; i < bitLength; i++)
        {
            var sourceBit = bitLength - 1 - i;
            var bitValue = (value >> sourceBit) & 1UL;
            var targetMask = (byte)(1 << bitInByte);
            if (bitValue == 0)
            {
                data[byteIndex] &= (byte)~targetMask;
            }
            else
            {
                data[byteIndex] |= targetMask;
            }

            MoveMotorolaCursor(ref byteIndex, ref bitInByte);
        }
    }

    private static void ValidateCommon(ReadOnlySpan<byte> data, int startBit, int bitLength)
    {
        if (data.Length <= 0)
        {
            throw new ArgumentException("Data span cannot be empty.", nameof(data));
        }

        if (startBit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startBit), "Start bit cannot be negative.");
        }

        if (bitLength is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), "Bit length must be in range 1..64.");
        }
    }

    private static void ValidateMotorolaRange(ReadOnlySpan<byte> data, int byteIndex, int bitInByte, int bitLength)
    {
        for (var i = 0; i < bitLength; i++)
        {
            if ((uint)byteIndex >= (uint)data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(data), "Target span is too small for the requested Motorola bit range.");
            }

            MoveMotorolaCursor(ref byteIndex, ref bitInByte);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UInt128 Mask128(int bitLength)
    {
        return bitLength == 64 ? ulong.MaxValue : ((UInt128)1 << bitLength) - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MoveMotorolaCursor(ref int byteIndex, ref int bitInByte)
    {
        bitInByte--;
        if (bitInByte >= 0)
        {
            return;
        }

        byteIndex++;
        bitInByte = 7;
    }
}
