namespace DiagKit.Dbc;

/// <summary>
/// 可缓存且对外不透明的 message runtime handle。<br/>
/// Cacheable opaque message runtime handle.
/// </summary>
public readonly record struct MessageHandle
{
    internal MessageHandle(int value, int documentToken, int channelToken)
    {
        Value = value;
        DocumentToken = documentToken;
        ChannelToken = channelToken;
    }

    internal int Value { get; }

    internal int DocumentToken { get; }

    internal int ChannelToken { get; }

    internal int Index => Value;
}

/// <summary>
/// 可缓存且对外不透明的 signal runtime handle。<br/>
/// Cacheable opaque signal runtime handle.
/// </summary>
public readonly record struct SignalHandle
{
    internal SignalHandle(int messageIndex, int signalIndex, int documentToken, int channelToken)
    {
        MessageIndex = messageIndex;
        SignalIndex = signalIndex;
        DocumentToken = documentToken;
        ChannelToken = channelToken;
    }

    internal int MessageIndex { get; }

    internal int SignalIndex { get; }

    internal int DocumentToken { get; }

    internal int ChannelToken { get; }
}
