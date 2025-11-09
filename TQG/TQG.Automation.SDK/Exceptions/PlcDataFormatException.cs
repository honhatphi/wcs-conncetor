namespace TQG.Automation.SDK.Exceptions;

/// <summary>
/// Exception thrown when data type mismatch or format error occurs during read/write operations.
/// </summary>
public class PlcDataFormatException : PlcException
{
    /// <summary>
    /// Initializes a new instance of the PlcDataFormatException class.
    /// </summary>
    public PlcDataFormatException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PlcDataFormatException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PlcDataFormatException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PlcDataFormatException class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PlcDataFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
