namespace DiagKit.Dbc;

/// <summary>
/// DBC 消息/信号的发送类型，映射自 GenMsgSendType / GenSigSendType 属性语义。<br/>
/// DBC message/signal send type, mapped from GenMsgSendType / GenSigSendType attribute semantics.
/// </summary>
public enum DbcSendType
{
    /// <summary>
    /// 未知或未指定 / Unknown or unspecified.
    /// </summary>
    Unknown,

    /// <summary>
    /// 无发送 / No sending.
    /// </summary>
    None,

    /// <summary>
    /// 周期发送 / Cyclic sending.
    /// </summary>
    Cyclic,

    /// <summary>
    /// 事件触发发送 / Event-triggered sending.
    /// </summary>
    Event,

    /// <summary>
    /// 激活时周期发送 / Cyclic sending when active.
    /// </summary>
    CyclicIfActive,

    /// <summary>
    /// 周期并事件触发 / Cyclic and event-triggered.
    /// </summary>
    CyclicAndEvent,

    /// <summary>
    /// 激活时发送 / Send when active.
    /// </summary>
    IfActive,

    /// <summary>
    /// 写入时发送 / Send on write.
    /// </summary>
    OnWrite,

    /// <summary>
    /// 写入时带重复发送 / Send on write with repetition.
    /// </summary>
    OnWriteWithRepetition,

    /// <summary>
    /// 值变化时发送 / Send on change.
    /// </summary>
    OnChange,

    /// <summary>
    /// 值变化时带重复发送 / Send on change with repetition.
    /// </summary>
    OnChangeWithRepetition,

    /// <summary>
    /// 激活时带重复发送 / Send when active with repetition.
    /// </summary>
    IfActiveWithRepetition,
}
