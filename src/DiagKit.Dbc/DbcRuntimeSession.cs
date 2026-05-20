namespace DiagKit.Dbc;

/// <summary>
/// 基于同一个 DBC document 创建一个或多个隔离 channel runtime 的运行时会话。<br/>
/// Runtime session that creates one or more isolated channel runtimes from a single DBC document.
/// </summary>
public sealed class DbcRuntimeSession
{
    private DbcRuntimeSession(DbcDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// 会话关联的不可变 DBC 文档 / The immutable DBC document associated with this session.
    /// </summary>
    public DbcDocument Document { get; }

    /// <summary>
    /// 从不可变 DBC document 创建 runtime session。<br/>
    /// Creates a runtime session from an immutable DBC document.
    /// </summary>
    public static DbcRuntimeSession Create(DbcDocument document)
    {
        return new DbcRuntimeSession(document);
    }

    /// <summary>
    /// 创建一个独立 channel runtime。调用方应按单写者模型驱动每个 channel。<br/>
    /// Creates an independent channel runtime. Callers should drive each channel under a single-writer model.
    /// </summary>
    public DbcChannelRuntime CreateChannel(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DbcChannelRuntime(this, name);
    }
}
