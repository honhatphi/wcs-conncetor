namespace TQG.Automation.SDK.Exceptions;

/// <summary>
/// Exception thrown when a PLC address is invalid or malformed.
/// </summary>
public class PlcInvalidAddressException : PlcException
{
    /// <summary>
    /// Initializes a new instance of the PlcInvalidAddressException class.
    /// </summary>
    public PlcInvalidAddressException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PlcInvalidAddressException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PlcInvalidAddressException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PlcInvalidAddressException class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PlcInvalidAddressException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
