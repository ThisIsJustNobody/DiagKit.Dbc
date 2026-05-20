namespace DiagKit.Dbc;

/// <summary>
/// 使用 "Message.Signal" 形式定位一个 DBC signal 的轻量路径值对象。<br/>
/// Lightweight value object locating a DBC signal using the "Message.Signal" form.
/// </summary>
public readonly record struct SignalPath
{
    /// <summary>
    /// 创建 signal path。<br/>
    /// Creates a signal path.
    /// </summary>
    public SignalPath(string messageName, string signalName)
    {
        MessageName = string.IsNullOrWhiteSpace(messageName)
            ? throw new ArgumentException("Message name cannot be empty.", nameof(messageName))
            : messageName;
        SignalName = string.IsNullOrWhiteSpace(signalName)
            ? throw new ArgumentException("Signal name cannot be empty.", nameof(signalName))
            : signalName;
    }

    /// <summary>
    /// Message 名称。<br/>
    /// Message name.
    /// </summary>
    public string MessageName { get; }

    /// <summary>
    /// Signal 名称。<br/>
    /// Signal name.
    /// </summary>
    public string SignalName { get; }

    /// <summary>
    /// 解析 "Message.Signal" 形式的 signal path，格式错误时抛出 FormatException。<br/>
    /// Parses a "Message.Signal" signal path, throwing FormatException on malformed input.
    /// </summary>
    public static SignalPath Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var path)
            ? path
            : throw new FormatException($"Signal path '{text}' must use the 'Message.Signal' form.");
    }

    /// <summary>
    /// 尝试解析 "Message.Signal" 形式的 signal path。<br/>
    /// Attempts to parse a "Message.Signal" signal path.
    /// </summary>
    public static bool TryParse(string? text, out SignalPath path)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            path = default;
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == text.Length - 1 || text.IndexOf('.', dot + 1) >= 0)
        {
            path = default;
            return false;
        }

        var messageName = text[..dot];
        var signalName = text[(dot + 1)..];
        if (string.IsNullOrWhiteSpace(messageName) || string.IsNullOrWhiteSpace(signalName))
        {
            path = default;
            return false;
        }

        path = new SignalPath(messageName, signalName);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{MessageName}.{SignalName}";
    }
}
