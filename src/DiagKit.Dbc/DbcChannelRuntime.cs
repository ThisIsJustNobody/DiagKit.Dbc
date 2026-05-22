namespace DiagKit.Dbc;

/// <summary>
/// 单个 CAN channel 的 DBC 运行时状态，负责信号值、接收解码、快照和周期 due-frame 轮询。<br/>
/// Per-channel DBC runtime state managing signal values, receive decode, snapshots, and periodic due-frame polling.
/// </summary>
public sealed class DbcChannelRuntime
{
    private static int nextChannelToken;

    private readonly DbcMessage[] messages;
    private readonly Dictionary<string, int[]> messageIndexesByName;
    private readonly Dictionary<CanIdentifier, int> messageIndexesByIdentifier;
    private readonly MessageRuntimeState[] states;
    private readonly bool[] observingMessages;
    private readonly object stateGate = new();
    private readonly int channelToken;
    private readonly int maxDataLength;
    private bool hasObservingFilter;

    internal DbcChannelRuntime(DbcRuntimeSession session, string name)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Name = name;
        channelToken = System.Threading.Interlocked.Increment(ref nextChannelToken);

        messages = new DbcMessage[session.Document.Messages.Count];
        var messageIndexListsByName = new Dictionary<string, List<int>>(messages.Length, StringComparer.Ordinal);
        messageIndexesByIdentifier = new Dictionary<CanIdentifier, int>(messages.Length);
        states = new MessageRuntimeState[messages.Length];
        observingMessages = new bool[messages.Length];

        for (var i = 0; i < messages.Length; i++)
        {
            var message = session.Document.Messages[i];
            messages[i] = message;
            foreach (var lookupName in DbcNameLookup.EnumerateLookupNames(message.Name, message.NameAliases))
            {
                if (!messageIndexListsByName.TryGetValue(lookupName, out var indexes))
                {
                    indexes = [];
                    messageIndexListsByName.Add(lookupName, indexes);
                }

                indexes.Add(i);
            }

            messageIndexesByIdentifier.Add(message.Identifier, i);
            var runtimeDataLength = message.SupportsSingleFrameRuntime ? message.DataLength : 0;
            states[i] = new MessageRuntimeState(runtimeDataLength);
            maxDataLength = Math.Max(maxDataLength, runtimeDataLength);
        }

        messageIndexesByName = messageIndexListsByName.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 所属的 runtime session / The parent runtime session.
    /// </summary>
    public DbcRuntimeSession Session { get; }

    /// <summary>
    /// channel 名称 / Channel name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 按消息名解析可缓存的 runtime message handle。<br/>
    /// Resolves a cacheable runtime message handle by message name.
    /// </summary>
    public bool TryResolveMessage(string messageName, out MessageHandle handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        if (messageIndexesByName.TryGetValue(messageName, out var indexes) &&
            indexes.Length == 1 &&
            messages[indexes[0]].SupportsSingleFrameRuntime)
        {
            handle = new MessageHandle(indexes[0], Session.Document.RuntimeToken, channelToken);
            return true;
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// 按 normalized CAN identifier 解析可缓存的 runtime message handle。<br/>
    /// Resolves a cacheable runtime message handle by normalized CAN identifier.
    /// </summary>
    public bool TryResolveMessage(CanIdentifier identifier, out MessageHandle handle)
    {
        if (messageIndexesByIdentifier.TryGetValue(identifier, out var index) &&
            messages[index].SupportsSingleFrameRuntime)
        {
            handle = new MessageHandle(index, Session.Document.RuntimeToken, channelToken);
            return true;
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// 按消息名解析 message handle，找不到时抛出 DbcException。<br/>
    /// Resolves a message handle by name, throws DbcException if not found.
    /// </summary>
    public MessageHandle ResolveMessage(string messageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        if (messageIndexesByName.TryGetValue(messageName, out var indexes))
        {
            if (indexes.Length > 1)
            {
                throw new DbcException($"Message '{messageName}' is ambiguous in channel '{Name}'. Use Document.FindMessages(...) to enumerate candidates.");
            }

            if (!messages[indexes[0]].SupportsSingleFrameRuntime)
            {
                throw CreateRuntimeUnsupportedMessageException(messages[indexes[0]]);
            }
        }

        return TryResolveMessage(messageName, out var handle)
            ? handle
            : throw new DbcException($"Message '{messageName}' was not found in channel '{Name}'. DBC name lookup is case-sensitive; check Document.Messages for available message names.");
    }

    /// <summary>
    /// 按 CAN identifier 解析 message handle，找不到时抛出 DbcException。<br/>
    /// Resolves a message handle by CAN identifier, throws DbcException if not found.
    /// </summary>
    public MessageHandle ResolveMessage(CanIdentifier identifier)
    {
        if (messageIndexesByIdentifier.TryGetValue(identifier, out var index) &&
            !messages[index].SupportsSingleFrameRuntime)
        {
            throw CreateRuntimeUnsupportedMessageException(messages[index]);
        }

        return TryResolveMessage(identifier, out var handle)
            ? handle
            : throw new DbcException($"Message '{identifier}' was not found in channel '{Name}'. Check Document.Messages for available CAN identifiers.");
    }

    /// <summary>
    /// 在指定 message 中按 signal name 解析可缓存的 runtime signal handle。<br/>
    /// Resolves a cacheable runtime signal handle by signal name within a given message.
    /// </summary>
    public bool TryResolveSignal(MessageHandle messageHandle, string signalName, out SignalHandle handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        var message = GetMessage(messageHandle);
        var matchIndex = -1;
        for (var i = 0; i < message.Signals.Count; i++)
        {
            if (DbcNameLookup.Matches(message.Signals[i].Name, message.Signals[i].NameAliases, signalName))
            {
                if (matchIndex >= 0)
                {
                    handle = default;
                    return false;
                }

                matchIndex = i;
            }
        }

        if (matchIndex >= 0)
        {
            handle = new SignalHandle(messageHandle.Index, matchIndex, Session.Document.RuntimeToken, channelToken);
            return true;
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// 在指定 message 中按 signal name 解析可缓存的 runtime signal handle。<br/>
    /// Resolves a cacheable runtime signal handle by signal name within a given message.
    /// </summary>
    public SignalHandle ResolveSignal(MessageHandle messageHandle, string signalName)
    {
        var message = GetMessage(messageHandle);
        var matchCount = 0;
        var matchIndex = -1;
        for (var i = 0; i < message.Signals.Count; i++)
        {
            if (!DbcNameLookup.Matches(message.Signals[i].Name, message.Signals[i].NameAliases, signalName))
            {
                continue;
            }

            matchCount++;
            matchIndex = i;
        }

        return matchCount switch
        {
            1 => new SignalHandle(messageHandle.Index, matchIndex, Session.Document.RuntimeToken, channelToken),
            > 1 => throw new DbcException($"Signal '{signalName}' is ambiguous in message '{message.Name}'. Use object-based runtime handle resolution."),
            _ => throw new DbcException($"Signal '{message.Name}.{signalName}' was not found. DBC name lookup is case-sensitive; check message '{message.Name}' Signals for available signal names."),
        };
    }

    /// <summary>
    /// 按 signal path 解析可缓存的 runtime signal handle。<br/>
    /// Resolves a cacheable runtime signal handle by signal path.
    /// </summary>
    public bool TryResolveSignal(SignalPath signalPath, out SignalHandle handle)
    {
        if (TryResolveMessage(signalPath.MessageName, out var messageHandle))
        {
            return TryResolveSignal(messageHandle, signalPath.SignalName, out handle);
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// 按 signal path 解析 runtime signal handle，找不到或歧义时抛出 DbcException。<br/>
    /// Resolves a runtime signal handle by signal path, throwing DbcException when missing or ambiguous.
    /// </summary>
    public SignalHandle ResolveSignal(SignalPath signalPath)
    {
        var messageHandle = ResolveMessage(signalPath.MessageName);
        return ResolveSignal(messageHandle, signalPath.SignalName);
    }

    /// <summary>
    /// 通过 DbcSignal 对象解析可缓存的 runtime signal handle，适用于同名 signal 歧义场景。<br/>
    /// Resolves a cacheable runtime signal handle from a DbcSignal object, suitable for duplicate-name ambiguity.
    /// </summary>
    public bool TryResolveSignal(MessageHandle messageHandle, DbcSignal signal, out SignalHandle handle)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var message = GetMessage(messageHandle);
        if (!ReferenceEquals(signal.Message, message))
        {
            handle = default;
            return false;
        }

        for (var i = 0; i < message.Signals.Count; i++)
        {
            if (ReferenceEquals(message.Signals[i], signal))
            {
                handle = new SignalHandle(messageHandle.Index, i, Session.Document.RuntimeToken, channelToken);
                return true;
            }
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// 通过 DbcSignal 对象解析 runtime signal handle，找不到时抛出 DbcException。<br/>
    /// Resolves a runtime signal handle from a DbcSignal object, throwing DbcException if not found.
    /// </summary>
    public SignalHandle ResolveSignal(MessageHandle messageHandle, DbcSignal signal)
    {
        return TryResolveSignal(messageHandle, signal, out var handle)
            ? handle
            : throw new DbcException($"Signal '{signal.Name}' does not belong to message handle '{messageHandle.Value}' in channel '{Name}'.");
    }

    /// <summary>
    /// 设置 signal 物理值，并同步更新对应 message payload。<br/>
    /// Sets the signal physical value and synchronizes the message payload.
    /// </summary>
    public SignalWriteResult SetPhysicalValue(
        SignalHandle signalHandle,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        var (_, signal, state) = GetSignalState(signalHandle);
        lock (stateGate)
        {
            var result = signal.TryEncodePhysical(state.Data, physicalValue, policy);
            if (!result.Succeeded)
            {
                return result;
            }

            state.HasData = true;
            state.Timestamp = timestamp;
            return result;
        }
    }

    /// <summary>
    /// 设置 signal raw value，并同步更新对应 message payload。<br/>
    /// Sets the signal raw value and synchronizes the message payload.
    /// </summary>
    public void SetRawValue(SignalHandle signalHandle, ulong rawValue, DbcTimestamp timestamp = default)
    {
        var (_, signal, state) = GetSignalState(signalHandle);
        lock (stateGate)
        {
            signal.EncodeRaw(state.Data, rawValue);
            state.HasData = true;
            state.Timestamp = timestamp;
        }
    }

    /// <summary>
    /// 将指定 message 加入发布集合，之后由 PollDueFrames 产出 due frames。<br/>
    /// Adds a message to the publishing set for subsequent due-frame production via PollDueFrames.
    /// </summary>
    public void AddPublishingMessage(
        MessageHandle messageHandle,
        TimeSpan? period = null,
        DbcTimestamp firstDueTime = default)
    {
        var message = GetMessage(messageHandle);
        var resolvedPeriod = period ?? GetDbcCycleTime(message);
        if (resolvedPeriod <= TimeSpan.Zero)
        {
            throw new DbcException($"Message '{message.Name}' does not define a positive publishing period.");
        }

        lock (stateGate)
        {
            states[messageHandle.Index].Schedule = new PeriodicMessageState(
                resolvedPeriod.Ticks,
                firstDueTime.Ticks,
                firstDueTime.Kind,
                firstDueTime.Kind != DbcTimestampKind.Unspecified);
        }
    }

    /// <summary>
    /// 判断指定 message 是否已加入发布集合。<br/>
    /// Checks whether a message has been added to the publishing set.
    /// </summary>
    public bool IsPublishing(MessageHandle messageHandle)
    {
        _ = GetMessage(messageHandle);
        lock (stateGate)
        {
            return states[messageHandle.Index].Schedule is not null;
        }
    }

    /// <summary>
    /// 显式按 DBC send type 与 cycle time 注册明确周期类 message。<br/>
    /// Explicitly registers unambiguously cyclic messages from the DBC by send type and cycle time.
    /// </summary>
    public DbcPublishingRegistrationReport RegisterCyclicPublishingMessagesFromDbc(DbcTimestamp firstDueTime = default)
    {
        var entries = new List<DbcPublishingRegistrationEntry>(messages.Length);
        lock (stateGate)
        {
            for (var i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                if (!message.SupportsSingleFrameRuntime)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedRuntimeUnsupported,
                        GetDbcCycleTime(message),
                        "Message is not supported by the CAN/CAN FD single-frame runtime."));
                    continue;
                }

                if (states[i].Schedule is not null)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.AlreadyRegistered,
                        GetSchedulePeriod(i),
                        "Message is already registered for publishing."));
                    continue;
                }

                var period = GetDbcCycleTime(message);
                if (period <= TimeSpan.Zero)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedNoCycleTime,
                        period,
                        "Message does not define a positive GenMsgCycleTime."));
                    continue;
                }

                if (!IsUnambiguousCyclicSendType(message.SendType))
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedSendType,
                        period,
                        $"Message SendType '{message.SendType}' is not unambiguously cyclic."));
                    continue;
                }

                RegisterPublishingMessage(i, period, firstDueTime);
                entries.Add(CreatePublishingEntry(
                    message,
                    DbcPublishingRegistrationStatus.Registered,
                    period,
                    "Message registered for publishing."));
            }
        }

        return new DbcPublishingRegistrationReport(entries);
    }

    /// <summary>
    /// 显式按 DBC send type 与 cycle time 注册明确周期类 message，并返回新增注册数量。<br/>
    /// Explicitly registers unambiguously cyclic DBC messages and returns the number of newly registered messages.
    /// </summary>
    public int AddCyclicPublishingMessagesFromDbc(DbcTimestamp firstDueTime = default)
    {
        return RegisterCyclicPublishingMessagesFromDbc(firstDueTime).RegisteredCount;
    }

    /// <summary>
    /// 显式注册所有定义了正 GenMsgCycleTime 的 message，不要求 DBC send type 为周期类。<br/>
    /// Explicitly registers all messages with a positive GenMsgCycleTime, without requiring a cyclic DBC send type.
    /// </summary>
    public DbcPublishingRegistrationReport RegisterCycleTimePublishingMessagesFromDbc(DbcTimestamp firstDueTime = default)
    {
        var entries = new List<DbcPublishingRegistrationEntry>(messages.Length);
        lock (stateGate)
        {
            for (var i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                if (!message.SupportsSingleFrameRuntime)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedRuntimeUnsupported,
                        GetDbcCycleTime(message),
                        "Message is not supported by the CAN/CAN FD single-frame runtime."));
                    continue;
                }

                if (states[i].Schedule is not null)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.AlreadyRegistered,
                        GetSchedulePeriod(i),
                        "Message is already registered for publishing."));
                    continue;
                }

                var period = GetDbcCycleTime(message);
                if (period <= TimeSpan.Zero)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedNoCycleTime,
                        period,
                        "Message does not define a positive GenMsgCycleTime."));
                    continue;
                }

                RegisterPublishingMessage(i, period, firstDueTime);
                entries.Add(CreatePublishingEntry(
                    message,
                    DbcPublishingRegistrationStatus.Registered,
                    period,
                    "Message registered for publishing."));
            }
        }

        return new DbcPublishingRegistrationReport(entries);
    }

    /// <summary>
    /// 显式注册所有定义了正 GenMsgCycleTime 的 message，并返回新增注册数量。<br/>
    /// Explicitly registers all messages with a positive GenMsgCycleTime and returns the number of newly registered messages.
    /// </summary>
    public int AddCycleTimePublishingMessagesFromDbc(DbcTimestamp firstDueTime = default)
    {
        return RegisterCycleTimePublishingMessagesFromDbc(firstDueTime).RegisteredCount;
    }

    /// <summary>
    /// 按发送节点批量注册发布 message；未显式指定 period 时使用 GenMsgCycleTime。<br/>
    /// Registers publishing messages transmitted by the given node; when period is omitted, GenMsgCycleTime is used.
    /// </summary>
    public DbcPublishingRegistrationReport RegisterPublishingMessagesTransmittedBy(
        string nodeName,
        TimeSpan? period = null,
        DbcTimestamp firstDueTime = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        if (period is { } explicitPeriod && explicitPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Publishing period must be positive.");
        }

        var entries = new List<DbcPublishingRegistrationEntry>(messages.Length);
        lock (stateGate)
        {
            for (var i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                if (!IsTransmittedBy(message, nodeName))
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedNodeMismatch,
                        period ?? GetDbcCycleTime(message),
                        $"Message is not transmitted by node '{nodeName}'."));
                    continue;
                }

                if (!message.SupportsSingleFrameRuntime)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedRuntimeUnsupported,
                        period ?? GetDbcCycleTime(message),
                        "Message is not supported by the CAN/CAN FD single-frame runtime."));
                    continue;
                }

                if (states[i].Schedule is not null)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.AlreadyRegistered,
                        GetSchedulePeriod(i),
                        "Message is already registered for publishing."));
                    continue;
                }

                var resolvedPeriod = period ?? GetDbcCycleTime(message);
                if (resolvedPeriod <= TimeSpan.Zero)
                {
                    entries.Add(CreatePublishingEntry(
                        message,
                        DbcPublishingRegistrationStatus.SkippedNoCycleTime,
                        resolvedPeriod,
                        "Message does not define a positive publishing period."));
                    continue;
                }

                RegisterPublishingMessage(i, resolvedPeriod, firstDueTime);
                entries.Add(CreatePublishingEntry(
                    message,
                    DbcPublishingRegistrationStatus.Registered,
                    resolvedPeriod,
                    "Message registered for publishing."));
            }
        }

        return new DbcPublishingRegistrationReport(entries);
    }

    /// <summary>
    /// 按发送节点批量注册发布 message，并返回新增注册数量。<br/>
    /// Registers publishing messages transmitted by the given node and returns the number of newly registered messages.
    /// </summary>
    public int AddPublishingMessagesTransmittedBy(
        string nodeName,
        TimeSpan? period = null,
        DbcTimestamp firstDueTime = default)
    {
        return RegisterPublishingMessagesTransmittedBy(nodeName, period, firstDueTime).RegisteredCount;
    }

    /// <summary>
    /// 将指定 message 加入观察过滤集合（白名单模式）。<br/>
    /// Adds a message to the observing filter set (whitelist mode).
    /// </summary>
    public void AddObservingMessage(MessageHandle messageHandle)
    {
        _ = GetMessage(messageHandle);
        lock (stateGate)
        {
            observingMessages[messageHandle.Index] = true;
            hasObservingFilter = true;
        }
    }

    /// <summary>
    /// 判断指定 message 是否通过观察过滤器。未设置过滤时默认允许所有消息。<br/>
    /// Checks whether a message passes the observing filter. All messages are allowed when no filter is set.
    /// </summary>
    public bool IsObserving(MessageHandle messageHandle)
    {
        _ = GetMessage(messageHandle);
        lock (stateGate)
        {
            return !hasObservingFilter || observingMessages[messageHandle.Index];
        }
    }

    /// <summary>
    /// 轮询当前到期的周期帧。错过的历史周期不会补发。<br/>
    /// Polls currently due periodic frames. Missed historical cycles are skipped (not backfilled).
    /// </summary>
    public int PollDueFrames(DbcTimestamp now, IDbcFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var emitted = 0;
        Span<byte> frameBuffer = stackalloc byte[maxDataLength];
        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            if (!message.SupportsSingleFrameRuntime)
            {
                continue;
            }

            var data = frameBuffer[..message.DataLength];
            if (!TryPrepareDueFrame(i, now, data))
            {
                continue;
            }

            sink.OnFrame(message.Identifier, data, message.FrameFlags, now);
            emitted++;
        }

        return emitted;
    }

    /// <summary>
    /// 立即按当前 runtime payload 构建一帧 message。<br/>
    /// Immediately builds a frame from the current runtime payload for the given message.
    /// </summary>
    public void BuildFrameNow(MessageHandle messageHandle, DbcTimestamp timestamp, IDbcFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var message = GetMessage(messageHandle);
        Span<byte> data = stackalloc byte[message.DataLength];
        lock (stateGate)
        {
            states[messageHandle.Index].Data.AsSpan(0, message.DataLength).CopyTo(data);
        }

        sink.OnFrame(message.Identifier, data, message.FrameFlags, timestamp);
    }

    /// <summary>
    /// 获取 message 当前快照，不执行 timeout stale 判定。<br/>
    /// Gets the current message snapshot without timeout stale evaluation.
    /// </summary>
    public MessageSnapshot GetMessageSnapshot(MessageHandle messageHandle)
    {
        return GetMessageSnapshot(messageHandle, DbcTimestamp.Unspecified);
    }

    /// <summary>
    /// 获取 message 当前快照，并使用调用方提供的当前时间执行 timeout stale 判定。<br/>
    /// Gets the current message snapshot with timeout stale evaluation using the caller-provided current time.
    /// </summary>
    public MessageSnapshot GetMessageSnapshot(MessageHandle messageHandle, DbcTimestamp now)
    {
        var message = GetMessage(messageHandle);
        var data = new byte[message.DataLength];
        DbcTimestamp timestamp;
        bool hasData;

        lock (stateGate)
        {
            var state = states[messageHandle.Index];
            state.Data.AsSpan(0, message.DataLength).CopyTo(data);
            timestamp = state.Timestamp;
            hasData = state.HasData;
        }

        return new MessageSnapshot(
            message.Identifier,
            message.Name,
            data,
            message.FrameFlags,
            hasData ? timestamp : DbcTimestamp.Unspecified,
            GetMessageQuality(message, hasData, timestamp, now));
    }

    /// <summary>
    /// 获取指定发布 message 的周期调度统计。<br/>
    /// Gets the periodic scheduling statistics for a publishing message.
    /// </summary>
    public DbcScheduleSnapshot GetScheduleSnapshot(MessageHandle messageHandle)
    {
        _ = GetMessage(messageHandle);
        lock (stateGate)
        {
            var schedule = states[messageHandle.Index].Schedule;
            if (schedule is null)
            {
                return new DbcScheduleSnapshot(
                    false,
                    TimeSpan.Zero,
                    DbcTimestamp.Unspecified,
                    DbcTimestamp.Unspecified,
                    0,
                    0,
                    0,
                    0);
            }

            return new DbcScheduleSnapshot(
                true,
                TimeSpan.FromTicks(schedule.PeriodTicks),
                schedule.HasNextDueTime
                    ? new DbcTimestamp(schedule.NextDueTicks, schedule.ClockKind)
                    : DbcTimestamp.Unspecified,
                schedule.LastEmittedTime,
                schedule.EmittedCount,
                schedule.DeadlineMissCount,
                schedule.MissedCycleCount,
                schedule.LastJitterTicks);
        }
    }

    /// <summary>
    /// 获取 signal 当前快照，不执行 timeout stale 判定。<br/>
    /// Gets the current signal snapshot without timeout stale evaluation.
    /// </summary>
    public SignalSnapshot GetSignalSnapshot(SignalHandle signalHandle)
    {
        return GetSignalSnapshot(signalHandle, DbcTimestamp.Unspecified);
    }

    /// <summary>
    /// 获取 signal 当前快照，并使用调用方提供的当前时间执行 timeout stale 判定。<br/>
    /// Gets the current signal snapshot with timeout stale evaluation using the caller-provided current time.
    /// </summary>
    public SignalSnapshot GetSignalSnapshot(SignalHandle signalHandle, DbcTimestamp now)
    {
        var (message, signal, state) = GetSignalState(signalHandle);
        Span<byte> data = stackalloc byte[message.DataLength];
        DbcTimestamp timestamp;
        bool hasData;

        lock (stateGate)
        {
            hasData = state.HasData;
            timestamp = state.Timestamp;
            if (hasData)
            {
                state.Data.AsSpan(0, message.DataLength).CopyTo(data);
            }
        }

        if (!hasData)
        {
            return new SignalSnapshot(
                message.Identifier,
                message.Name,
                signal.Name,
                DbcTimestamp.Unspecified,
                0,
                double.NaN,
                SignalQuality.NoData);
        }

        if (!DbcMessage.IsSignalActive(message, data, signal))
        {
            return new SignalSnapshot(
                message.Identifier,
                message.Name,
                signal.Name,
                timestamp,
                0,
                double.NaN,
                SignalQuality.InactiveMultiplex);
        }

        var rawValue = signal.DecodeRaw(data);
        return new SignalSnapshot(
            message.Identifier,
            message.Name,
            signal.Name,
            timestamp,
            rawValue,
            signal.RawToPhysical(rawValue),
            GetSignalQuality(message, signal, timestamp, now));
    }

    /// <summary>
    /// 获取 signal 的 UI-friendly 当前值与元数据快照。<br/>
    /// Gets a UI-friendly snapshot combining the signal's current value and metadata.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(SignalHandle signalHandle)
    {
        return GetSignalViewSnapshot(signalHandle, DbcTimestamp.Unspecified);
    }

    /// <summary>
    /// 获取 signal 的 UI-friendly 当前值与元数据快照，并使用调用方提供的当前时间执行 timeout stale 判定。<br/>
    /// Gets a UI-friendly snapshot combining current value and metadata with timeout evaluation.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(SignalHandle signalHandle, DbcTimestamp now)
    {
        var (_, signal, _) = GetSignalState(signalHandle);
        var snapshot = GetSignalSnapshot(signalHandle, now);
        return new DbcSignalViewSnapshot(
            snapshot.Identifier,
            snapshot.MessageName,
            snapshot.SignalName,
            snapshot.Timestamp,
            snapshot.RawValue,
            snapshot.PhysicalValue,
            snapshot.Quality,
            signal.Unit,
            signal.Minimum,
            signal.Maximum,
            signal.ValueDescriptions);
    }

    /// <summary>
    /// 处理一帧接收报文，并可选地把解码后的 signal samples 写入 sink。<br/>
    /// Processes a received frame and optionally writes decoded signal samples to a sink.
    /// </summary>
    public int ProcessReceivedFrame(DbcFrameView frame, ISignalSampleSink? sink = null)
    {
        if (!messageIndexesByIdentifier.TryGetValue(frame.Identifier, out var messageIndex))
        {
            return 0;
        }

        var message = messages[messageIndex];
        if (!message.SupportsSingleFrameRuntime)
        {
            return 0;
        }

        if (frame.Data.Length < message.DataLength)
        {
            throw new ArgumentException($"Frame '{frame.Identifier}' has {frame.Data.Length} bytes, but message '{message.Name}' needs {message.DataLength} bytes.", nameof(frame));
        }

        var frameData = frame.Data[..message.DataLength];
        lock (stateGate)
        {
            if (hasObservingFilter && !observingMessages[messageIndex])
            {
                return 0;
            }

            var state = states[messageIndex];
            frameData.CopyTo(state.Data);
            state.HasData = true;
            state.Timestamp = frame.Timestamp;
        }

        if (sink is null)
        {
            return message.Signals.Count;
        }

        for (var i = 0; i < message.Signals.Count; i++)
        {
            var signal = message.Signals[i];
            var sample = CreateSignalSample(message, frameData, signal, frame.Timestamp);
            sink.OnSignalSample(in sample);
        }

        return message.Signals.Count;
    }

    private static SignalSample CreateSignalSample(
        DbcMessage message,
        ReadOnlySpan<byte> data,
        DbcSignal signal,
        DbcTimestamp timestamp)
    {
        if (!DbcMessage.IsSignalActive(message, data, signal))
        {
            return new SignalSample(
                message.Identifier,
                message.Name,
                signal.Name,
                timestamp,
                0,
                double.NaN,
                SignalQuality.InactiveMultiplex);
        }

        var rawValue = signal.DecodeRaw(data);
        return new SignalSample(
            message.Identifier,
            message.Name,
            signal.Name,
            timestamp,
            rawValue,
            signal.RawToPhysical(rawValue),
            SignalQuality.Valid);
    }

    private static TimeSpan GetDbcCycleTime(DbcMessage message)
    {
        return message.CycleTimeMs is > 0
            ? TimeSpan.FromMilliseconds(message.CycleTimeMs.Value)
            : TimeSpan.Zero;
    }

    private static DbcPublishingRegistrationEntry CreatePublishingEntry(
        DbcMessage message,
        DbcPublishingRegistrationStatus status,
        TimeSpan period,
        string reason)
    {
        return new DbcPublishingRegistrationEntry(message.Name, message.Identifier, status, period, reason);
    }

    private void RegisterPublishingMessage(int messageIndex, TimeSpan period, DbcTimestamp firstDueTime)
    {
        states[messageIndex].Schedule = new PeriodicMessageState(
            period.Ticks,
            firstDueTime.Ticks,
            firstDueTime.Kind,
            firstDueTime.Kind != DbcTimestampKind.Unspecified);
    }

    private TimeSpan GetSchedulePeriod(int messageIndex)
    {
        var schedule = states[messageIndex].Schedule;
        return schedule is null
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(schedule.PeriodTicks);
    }

    private static bool IsTransmittedBy(DbcMessage message, string nodeName)
    {
        for (var i = 0; i < message.Transmitters.Count; i++)
        {
            var transmitter = message.Transmitters[i];
            if (DbcNameLookup.Matches(transmitter.Name, transmitter.NameAliases, nodeName))
            {
                return true;
            }
        }

        return false;
    }

    private static DbcException CreateRuntimeUnsupportedMessageException(DbcMessage message)
    {
        return new DbcException($"Message '{message.Name}' payload length {message.DataLength} is not supported by the CAN/CAN FD single-frame runtime.");
    }

    private bool TryPrepareDueFrame(int messageIndex, DbcTimestamp now, Span<byte> destination)
    {
        lock (stateGate)
        {
            var schedule = states[messageIndex].Schedule;
            if (schedule is null)
            {
                return false;
            }

            var message = messages[messageIndex];
            var clockKind = EnsureScheduleClockKind(schedule, now, message);
            var nextDueTicks = schedule.HasNextDueTime ? schedule.NextDueTicks : now.Ticks;
            if (now.Ticks < nextDueTicks)
            {
                return false;
            }

            var advance = CalculateScheduleAdvance(schedule, now, nextDueTicks, message);
            states[messageIndex].Data.AsSpan(0, message.DataLength).CopyTo(destination);
            ApplyScheduleAdvance(schedule, now, clockKind, advance);
            return true;
        }
    }

    private static SignalQuality GetMessageQuality(DbcMessage message, bool hasData, DbcTimestamp timestamp, DbcTimestamp now)
    {
        if (!hasData)
        {
            return SignalQuality.NoData;
        }

        return IsTimedOut(timestamp, now, message.TimeoutTimeMs)
            ? SignalQuality.Stale
            : SignalQuality.Valid;
    }

    private static SignalQuality GetSignalQuality(
        DbcMessage message,
        DbcSignal signal,
        DbcTimestamp timestamp,
        DbcTimestamp now)
    {
        var timeoutTimeMs = signal.TimeoutTimeMs ?? message.TimeoutTimeMs;
        return IsTimedOut(timestamp, now, timeoutTimeMs)
            ? SignalQuality.Stale
            : SignalQuality.Valid;
    }

    private static bool IsTimedOut(DbcTimestamp timestamp, DbcTimestamp now, int? timeoutTimeMs)
    {
        if (timeoutTimeMs is not > 0 ||
            timestamp.Kind == DbcTimestampKind.Unspecified ||
            now.Kind == DbcTimestampKind.Unspecified)
        {
            return false;
        }

        if (timestamp.Kind != now.Kind)
        {
            throw new DbcException(
                $"Cannot evaluate timeout between {timestamp.Kind} timestamp and {now.Kind} timestamp.");
        }

        if (now.Ticks < timestamp.Ticks)
        {
            throw new DbcException("Cannot evaluate timeout because the current timestamp is earlier than the sample timestamp.");
        }

        try
        {
            var elapsedTicks = checked(now.Ticks - timestamp.Ticks);
            return elapsedTicks > TimeSpan.FromMilliseconds(timeoutTimeMs.Value).Ticks;
        }
        catch (OverflowException ex)
        {
            throw new DbcException("Timeout arithmetic overflowed.", ex);
        }
    }

    private static bool IsUnambiguousCyclicSendType(DbcSendType sendType)
    {
        return sendType is DbcSendType.Cyclic or DbcSendType.CyclicAndEvent;
    }

    private static DbcTimestampKind EnsureScheduleClockKind(PeriodicMessageState schedule, DbcTimestamp now, DbcMessage message)
    {
        if (now.Kind == DbcTimestampKind.Unspecified)
        {
            throw new DbcException($"Message '{message.Name}' publishing schedule requires a specified poll timestamp kind.");
        }

        if (schedule.ClockKind == DbcTimestampKind.Unspecified)
        {
            return now.Kind;
        }

        if (now.Kind != schedule.ClockKind)
        {
            throw new DbcException(
                $"Message '{message.Name}' publishing schedule uses {schedule.ClockKind}, but poll timestamp uses {now.Kind}.");
        }

        return schedule.ClockKind;
    }

    private static ScheduleAdvance CalculateScheduleAdvance(
        PeriodicMessageState schedule,
        DbcTimestamp now,
        long nextDueTicks,
        DbcMessage message)
    {
        try
        {
            checked
            {
                var elapsedTicks = now.Ticks - nextDueTicks;
                var elapsedPeriods = (elapsedTicks / schedule.PeriodTicks) + 1;
                var skippedCycles = elapsedPeriods - 1;
                var emittedDueTicks = nextDueTicks + (skippedCycles * schedule.PeriodTicks);
                var lastJitterTicks = now.Ticks - emittedDueTicks;
                var nextScheduledDueTicks = nextDueTicks + (elapsedPeriods * schedule.PeriodTicks);
                var deadlineMissCount = schedule.DeadlineMissCount + (lastJitterTicks > 0 ? 1 : 0);

                return new ScheduleAdvance(
                    nextScheduledDueTicks,
                    schedule.EmittedCount + 1,
                    deadlineMissCount,
                    schedule.MissedCycleCount + skippedCycles,
                    lastJitterTicks);
            }
        }
        catch (OverflowException ex)
        {
            throw new DbcException($"Message '{message.Name}' publishing schedule arithmetic overflowed.", ex);
        }
    }

    private static void ApplyScheduleAdvance(
        PeriodicMessageState schedule,
        DbcTimestamp now,
        DbcTimestampKind clockKind,
        ScheduleAdvance advance)
    {
        schedule.ClockKind = clockKind;
        schedule.HasNextDueTime = true;
        schedule.LastEmittedTime = now;
        schedule.NextDueTicks = advance.NextDueTicks;
        schedule.EmittedCount = advance.EmittedCount;
        schedule.DeadlineMissCount = advance.DeadlineMissCount;
        schedule.MissedCycleCount = advance.MissedCycleCount;
        schedule.LastJitterTicks = advance.LastJitterTicks;
    }

    private DbcMessage GetMessage(MessageHandle handle)
    {
        if (handle.DocumentToken != Session.Document.RuntimeToken || handle.ChannelToken != channelToken)
        {
            throw new DbcException($"Message handle '{handle.Value}' is not valid for channel '{Name}'.");
        }

        if ((uint)handle.Index >= (uint)messages.Length)
        {
            throw new DbcException($"Message handle '{handle.Value}' is not valid for channel '{Name}'.");
        }

        return messages[handle.Index];
    }

    private (DbcMessage Message, DbcSignal Signal, MessageRuntimeState State) GetSignalState(SignalHandle handle)
    {
        if (handle.DocumentToken != Session.Document.RuntimeToken || handle.ChannelToken != channelToken)
        {
            throw new DbcException($"Signal handle '{handle.MessageIndex}:{handle.SignalIndex}' is not valid for channel '{Name}'.");
        }

        if ((uint)handle.MessageIndex >= (uint)messages.Length)
        {
            throw new DbcException($"Signal handle message index '{handle.MessageIndex}' is not valid for channel '{Name}'.");
        }

        var message = messages[handle.MessageIndex];
        if ((uint)handle.SignalIndex >= (uint)message.Signals.Count)
        {
            throw new DbcException($"Signal handle index '{handle.SignalIndex}' is not valid for message '{message.Name}'.");
        }

        return (message, message.Signals[handle.SignalIndex], states[handle.MessageIndex]);
    }

    private sealed class MessageRuntimeState(int dataLength)
    {
        public byte[] Data { get; } = new byte[dataLength];
        public bool HasData { get; set; }
        public DbcTimestamp Timestamp { get; set; } = DbcTimestamp.Unspecified;
        public PeriodicMessageState? Schedule { get; set; }
    }

    private readonly record struct ScheduleAdvance(
        long NextDueTicks,
        long EmittedCount,
        long DeadlineMissCount,
        long MissedCycleCount,
        long LastJitterTicks);

    private sealed class PeriodicMessageState(
        long periodTicks,
        long nextDueTicks,
        DbcTimestampKind clockKind,
        bool hasNextDueTime)
    {
        public long PeriodTicks { get; } = periodTicks;
        public long NextDueTicks { get; set; } = nextDueTicks;
        public DbcTimestampKind ClockKind { get; set; } = clockKind;
        public bool HasNextDueTime { get; set; } = hasNextDueTime;
        public DbcTimestamp LastEmittedTime { get; set; } = DbcTimestamp.Unspecified;
        public long EmittedCount { get; set; }
        public long DeadlineMissCount { get; set; }
        public long MissedCycleCount { get; set; }
        public long LastJitterTicks { get; set; }
    }
}
