namespace DiagKit.Dbc;

/// <summary>
/// 面向首次接入、脚本和 UI 工具的 DBC 加载 + simple channel 一体化 facade。<br/>
/// Integrated DBC loading plus simple-channel facade for first-use, scripting, and UI tooling scenarios.
/// </summary>
/// <remarks>
/// 本类型默认使用 Lenient loader，并在存在 Error 级 diagnostics 时失败关闭。高频路径应继续使用 DbcChannelRuntime 的 handle/span/sink API。<br/>
/// This type defaults to the Lenient loader and fails closed on Error diagnostics. Hot paths should keep using DbcChannelRuntime handle/span/sink APIs.
/// </remarks>
public sealed class DbcSimpleRuntime
{
    private DbcSimpleRuntime(DbcLoadResult loadResult, string channelName)
    {
        LoadResult = loadResult ?? throw new ArgumentNullException(nameof(loadResult));
        LoadResult.ThrowIfErrors();
        Document = LoadResult.Document ?? throw new DbcException("DBC load result did not contain a document.");
        Channel = DbcSimpleChannel.Create(Document, channelName);
    }

    /// <summary>
    /// 原始加载结果，包含 warning diagnostics。<br/>
    /// Original load result, including warning diagnostics.
    /// </summary>
    public DbcLoadResult LoadResult { get; }

    /// <summary>
    /// 加载后的不可变 DBC 文档。<br/>
    /// Loaded immutable DBC document.
    /// </summary>
    public DbcDocument Document { get; }

    /// <summary>
    /// Simple channel facade。<br/>
    /// Simple channel facade.
    /// </summary>
    public DbcSimpleChannel Channel { get; }

    /// <summary>
    /// 底层 runtime session。<br/>
    /// Underlying runtime session.
    /// </summary>
    public DbcRuntimeSession Session => Channel.Session;

    /// <summary>
    /// 底层 channel runtime。<br/>
    /// Underlying channel runtime.
    /// </summary>
    public DbcChannelRuntime RuntimeChannel => Channel.Channel;

    /// <summary>
    /// 从 DBC 文件加载 simple runtime。默认使用 Lenient 解析。<br/>
    /// Loads a simple runtime from a DBC file. Defaults to Lenient parsing.
    /// </summary>
    public static DbcSimpleRuntime LoadFile(
        string path,
        DbcLoadOptions? options = null,
        string channelName = "CAN1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        return new DbcSimpleRuntime(DbcLoader.LoadFile(path, options ?? DbcLoadOptions.Lenient), channelName);
    }

    /// <summary>
    /// 从 DBC 文件异步加载 simple runtime。默认使用 Lenient 解析。<br/>
    /// Asynchronously loads a simple runtime from a DBC file. Defaults to Lenient parsing.
    /// </summary>
    public static async Task<DbcSimpleRuntime> LoadFileAsync(
        string path,
        DbcLoadOptions? options = null,
        string channelName = "CAN1",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        var result = await DbcLoader.LoadFileAsync(path, options ?? DbcLoadOptions.Lenient, cancellationToken)
            .ConfigureAwait(false);
        return new DbcSimpleRuntime(result, channelName);
    }

    /// <summary>
    /// 从 DBC 文本加载 simple runtime。默认使用 Lenient 解析。<br/>
    /// Loads a simple runtime from DBC text. Defaults to Lenient parsing.
    /// </summary>
    public static DbcSimpleRuntime LoadText(
        string dbcText,
        DbcLoadOptions? options = null,
        string channelName = "CAN1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        return new DbcSimpleRuntime(DbcLoader.LoadText(dbcText, options ?? DbcLoadOptions.Lenient), channelName);
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径设置物理值。<br/>
    /// Sets a physical value by "Message.Signal" path.
    /// </summary>
    public SignalWriteResult SetPhysicalValue(
        string signalPath,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        return Channel.SetPhysicalValue(signalPath, physicalValue, policy, timestamp);
    }

    /// <summary>
    /// 通过 signal path 设置物理值。<br/>
    /// Sets a physical value by signal path.
    /// </summary>
    public SignalWriteResult SetPhysicalValue(
        SignalPath signalPath,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        return Channel.SetPhysicalValue(signalPath, physicalValue, policy, timestamp);
    }

    /// <summary>
    /// 尝试通过 "Message.Signal" 路径设置物理值。<br/>
    /// Attempts to set a physical value by "Message.Signal" path.
    /// </summary>
    public bool TrySetPhysicalValue(
        string signalPath,
        double physicalValue,
        out SignalWriteResult result,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        return Channel.TrySetPhysicalValue(signalPath, physicalValue, out result, policy, timestamp);
    }

    /// <summary>
    /// 尝试通过 signal path 设置物理值。<br/>
    /// Attempts to set a physical value by signal path.
    /// </summary>
    public bool TrySetPhysicalValue(
        SignalPath signalPath,
        double physicalValue,
        out SignalWriteResult result,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        return Channel.TrySetPhysicalValue(signalPath, physicalValue, out result, policy, timestamp);
    }

    /// <summary>
    /// 通过 message name 立即构建拥有 payload 的帧。<br/>
    /// Builds an owning frame immediately by message name.
    /// </summary>
    public DbcFrame BuildFrame(string messageName, DbcTimestamp timestamp = default)
    {
        return Channel.BuildFrame(messageName, timestamp);
    }

    /// <summary>
    /// 解码一帧并返回便捷读取结果。<br/>
    /// Decodes one frame and returns convenience values.
    /// </summary>
    public DbcSimpleFrameValues Decode(DbcFrameView frame)
    {
        return Channel.Decode(frame);
    }

    /// <summary>
    /// 处理一帧拥有型 frame，更新当前状态并返回便捷读取结果。<br/>
    /// Processes an owning frame, updates current state, and returns convenience values.
    /// </summary>
    public DbcSimpleFrameValues ProcessFrame(DbcFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return ProcessFrame(frame.Identifier, frame.Data, frame.Flags, frame.Timestamp);
    }

    /// <summary>
    /// 处理一帧 CAN/CAN FD 数据，更新当前状态并返回便捷读取结果。<br/>
    /// Processes one CAN/CAN FD frame, updates current state, and returns convenience values.
    /// </summary>
    public DbcSimpleFrameValues ProcessFrame(
        CanIdentifier identifier,
        ReadOnlySpan<byte> data,
        DbcFrameFlags flags = DbcFrameFlags.None,
        DbcTimestamp timestamp = default)
    {
        return Channel.Decode(new DbcFrameView(identifier, data, flags, timestamp));
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径读取当前物理值。<br/>
    /// Gets the current physical value by "Message.Signal" path.
    /// </summary>
    public double GetPhysicalValue(string signalPath, DbcTimestamp now = default)
    {
        return Channel.GetPhysicalValue(signalPath, now);
    }

    /// <summary>
    /// 通过 signal path 读取当前物理值。<br/>
    /// Gets the current physical value by signal path.
    /// </summary>
    public double GetPhysicalValue(SignalPath signalPath, DbcTimestamp now = default)
    {
        return Channel.GetPhysicalValue(signalPath, now);
    }

    /// <summary>
    /// 尝试通过 "Message.Signal" 路径读取当前物理值。<br/>
    /// Attempts to get the current physical value by "Message.Signal" path.
    /// </summary>
    public bool TryGetPhysicalValue(string signalPath, out double physicalValue, DbcTimestamp now = default)
    {
        return Channel.TryGetPhysicalValue(signalPath, out physicalValue, now);
    }

    /// <summary>
    /// 尝试通过 signal path 读取当前物理值。<br/>
    /// Attempts to get the current physical value by signal path.
    /// </summary>
    public bool TryGetPhysicalValue(SignalPath signalPath, out double physicalValue, DbcTimestamp now = default)
    {
        return Channel.TryGetPhysicalValue(signalPath, out physicalValue, now);
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径读取 UI-friendly signal 快照。<br/>
    /// Gets a UI-friendly signal snapshot by "Message.Signal" path.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(string signalPath, DbcTimestamp now = default)
    {
        return Channel.GetSignalViewSnapshot(signalPath, now);
    }

    /// <summary>
    /// 通过 signal path 读取 UI-friendly signal 快照。<br/>
    /// Gets a UI-friendly signal snapshot by signal path.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(SignalPath signalPath, DbcTimestamp now = default)
    {
        return Channel.GetSignalViewSnapshot(signalPath, now);
    }
}
