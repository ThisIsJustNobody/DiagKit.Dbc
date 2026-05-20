namespace DiagKit.Dbc;

/// <summary>
/// DBC 专用异常，用于标识 DBC 加载、解析和运行时错误。<br/>
/// DBC-specific exception for DBC loading, parsing, and runtime errors.
/// </summary>
public class DbcException : Exception
{
    /// <summary>
    /// 使用错误消息创建 DBC 异常。<br/>
    /// Creates a DBC exception with an error message.
    /// </summary>
    public DbcException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用错误消息和内部异常创建 DBC 异常。<br/>
    /// Creates a DBC exception with an error message and inner exception.
    /// </summary>
    public DbcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
