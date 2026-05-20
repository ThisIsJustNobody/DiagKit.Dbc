namespace DiagKit.Dbc;

internal static class DbcPhysicalCodec
{
    public static double Decode(DbcSignal signal, ulong rawValue)
    {
        return signal.ValueType switch
        {
            DbcSignalValueType.Signed => (SignExtend(rawValue, signal.BitLength) * signal.Factor) + signal.Offset,
            DbcSignalValueType.Unsigned => (rawValue * signal.Factor) + signal.Offset,
            DbcSignalValueType.Float => (DecodeSingle(signal, rawValue) * signal.Factor) + signal.Offset,
            DbcSignalValueType.Double => (DecodeDouble(signal, rawValue) * signal.Factor) + signal.Offset,
            _ => throw new ArgumentOutOfRangeException(nameof(signal), "Unsupported signal value type."),
        };
    }

    public static SignalWriteResult TryEncode(DbcSignal signal, double physicalValue, SignalWritePolicy policy)
    {
        try
        {
            signal.ValidateDefinition();
        }
        catch (InvalidOperationException ex)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.InvalidSignalDefinition, ex.Message);
        }

        if (!double.IsFinite(physicalValue))
        {
            return SignalWriteResult.Fail(SignalWriteStatus.NonFiniteValue, $"Signal '{signal.Name}' cannot encode a non-finite value.");
        }

        if (signal.Factor == 0)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.FactorIsZero, $"Signal '{signal.Name}' factor is 0.");
        }

        if (HasPhysicalRange(signal) && (physicalValue < signal.Minimum || physicalValue > signal.Maximum))
        {
            if (policy == SignalWritePolicy.Strict)
            {
                return SignalWriteResult.Fail(SignalWriteStatus.OutOfPhysicalRange, $"Signal '{signal.Name}' value is outside physical range.");
            }

            if (policy == SignalWritePolicy.ClampToPhysicalRange)
            {
                physicalValue = Math.Clamp(physicalValue, signal.Minimum, signal.Maximum);
            }
        }

        var scaledValue = (physicalValue - signal.Offset) / signal.Factor;
        if (!double.IsFinite(scaledValue))
        {
            return SignalWriteResult.Fail(SignalWriteStatus.NonFiniteValue, $"Signal '{signal.Name}' cannot encode a non-finite value.");
        }

        return signal.ValueType switch
        {
            DbcSignalValueType.Signed => TryEncodeSigned(signal, scaledValue, physicalValue, policy),
            DbcSignalValueType.Unsigned => TryEncodeUnsigned(signal, scaledValue, physicalValue, policy),
            DbcSignalValueType.Float => TryEncodeSingle(signal, scaledValue, physicalValue),
            DbcSignalValueType.Double => TryEncodeDouble(signal, scaledValue, physicalValue),
            _ => SignalWriteResult.Fail(SignalWriteStatus.InvalidSignalDefinition, $"Unsupported value type for signal '{signal.Name}'."),
        };
    }

    private static long SignExtend(ulong rawValue, int bitLength)
    {
        if (bitLength == 64)
        {
            return (long)rawValue;
        }

        var signBit = 1UL << (bitLength - 1);
        if ((rawValue & signBit) == 0)
        {
            return (long)rawValue;
        }

        var mask = ~((1UL << bitLength) - 1);
        return (long)(rawValue | mask);
    }

    private static bool HasPhysicalRange(DbcSignal signal)
    {
        return double.IsFinite(signal.Minimum) &&
            double.IsFinite(signal.Maximum) &&
            signal.Minimum <= signal.Maximum;
    }

    private static SignalWriteResult TryEncodeSigned(DbcSignal signal, double scaledValue, double physicalValue, SignalWritePolicy policy)
    {
        var (min, max) = GetSignedRange(signal.BitLength);
        var minDecimal = (decimal)min;
        var maxDecimal = (decimal)max;
        var roundedResult = RoundToRawInteger(scaledValue, minDecimal, maxDecimal, policy);

        if (!roundedResult.Succeeded)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.OutOfRawRange, $"Signal '{signal.Name}' value is outside raw range.");
        }

        var signedRawValue = (long)roundedResult.Value;
        var rawValue = (ulong)signedRawValue & GetMask(signal.BitLength);
        return SignalWriteResult.Success(rawValue, (signedRawValue * signal.Factor) + signal.Offset);
    }

    private static SignalWriteResult TryEncodeUnsigned(DbcSignal signal, double scaledValue, double physicalValue, SignalWritePolicy policy)
    {
        var max = GetMask(signal.BitLength);
        var roundedResult = RoundToRawInteger(scaledValue, 0, max, policy);

        if (!roundedResult.Succeeded)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.OutOfRawRange, $"Signal '{signal.Name}' value is outside raw range.");
        }

        var rawValue = (ulong)roundedResult.Value;
        return SignalWriteResult.Success(rawValue, (rawValue * signal.Factor) + signal.Offset);
    }

    private static (bool Succeeded, decimal Value) RoundToRawInteger(
        double scaledValue,
        decimal rawMinimum,
        decimal rawMaximum,
        SignalWritePolicy policy)
    {
        var rawMinimumAsDouble = (double)rawMinimum;
        var rawMaximumAsDouble = (double)rawMaximum;
        var canClampRaw = policy == SignalWritePolicy.ClampToRawRange;

        if (scaledValue < rawMinimumAsDouble)
        {
            return canClampRaw ? (true, rawMinimum) : (false, 0);
        }

        if (scaledValue > rawMaximumAsDouble)
        {
            return canClampRaw ? (true, rawMaximum) : (false, 0);
        }

        if (scaledValue == rawMinimumAsDouble)
        {
            return (true, rawMinimum);
        }

        if (scaledValue == rawMaximumAsDouble)
        {
            return (true, rawMaximum);
        }

        var rounded = decimal.Round((decimal)scaledValue, 0, MidpointRounding.AwayFromZero);

        if (rounded < rawMinimum)
        {
            return canClampRaw ? (true, rawMinimum) : (false, 0);
        }

        if (rounded > rawMaximum)
        {
            return canClampRaw ? (true, rawMaximum) : (false, 0);
        }

        return (true, rounded);
    }

    private static SignalWriteResult TryEncodeSingle(DbcSignal signal, double scaledValue, double physicalValue)
    {
        if (signal.BitLength != 32)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.InvalidSignalDefinition, $"Signal '{signal.Name}' must be 32 bits to encode Single.");
        }

        return SignalWriteResult.Success(BitConverter.SingleToUInt32Bits((float)scaledValue), physicalValue);
    }

    private static SignalWriteResult TryEncodeDouble(DbcSignal signal, double scaledValue, double physicalValue)
    {
        if (signal.BitLength != 64)
        {
            return SignalWriteResult.Fail(SignalWriteStatus.InvalidSignalDefinition, $"Signal '{signal.Name}' must be 64 bits to encode Double.");
        }

        return SignalWriteResult.Success(BitConverter.DoubleToUInt64Bits(scaledValue), physicalValue);
    }

    private static float DecodeSingle(DbcSignal signal, ulong rawValue)
    {
        if (signal.BitLength != 32)
        {
            throw new InvalidOperationException($"Signal '{signal.Name}' must be 32 bits to decode Single.");
        }

        return BitConverter.UInt32BitsToSingle((uint)rawValue);
    }

    private static double DecodeDouble(DbcSignal signal, ulong rawValue)
    {
        if (signal.BitLength != 64)
        {
            throw new InvalidOperationException($"Signal '{signal.Name}' must be 64 bits to decode Double.");
        }

        return BitConverter.UInt64BitsToDouble(rawValue);
    }

    private static ulong GetMask(int bitLength)
    {
        return bitLength == 64 ? ulong.MaxValue : (1UL << bitLength) - 1;
    }

    private static (long Min, long Max) GetSignedRange(int bitLength)
    {
        if (bitLength == 64)
        {
            return (long.MinValue, long.MaxValue);
        }

        var max = (1L << (bitLength - 1)) - 1;
        return (-max - 1, max);
    }
}
