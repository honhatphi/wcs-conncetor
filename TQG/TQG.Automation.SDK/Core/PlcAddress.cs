using System.Text.RegularExpressions;

namespace TQG.Automation.SDK.Core;

/// <summary>
/// Generated regex patterns for PLC address parsing.
/// </summary>
internal static partial class PlcAddressPatterns
{
    [GeneratedRegex(@"^DB(?<db>\d+)\.DB(?<type>[XBWD])(?<offset>\d+)$", RegexOptions.IgnoreCase)]
    internal static partial Regex AddressPattern();

    [GeneratedRegex(@"^DB(?<db>\d+)\.DBX(?<offset>\d+)\.(?<bit>\d+)$", RegexOptions.IgnoreCase)]
    internal static partial Regex BitAddressPattern();
}

/// <summary>
/// Immutable structure representing a Siemens S7 PLC memory address.
/// Supports Data Block (DB) addresses with various data types.
/// </summary>
internal readonly record struct PlcAddress
{

    /// <summary>
    /// Gets the data block number.
    /// </summary>
    public int DataBlock { get; init; }

    /// <summary>
    /// Gets the data type (X=Bit, B=Byte, W=Word, D=DWord).
    /// </summary>
    public char DataType { get; init; }

    /// <summary>
    /// Gets the byte offset within the data block.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets the bit offset (only valid for bit addresses, DataType='X').
    /// </summary>
    public int BitOffset { get; init; }

    /// <summary>
    /// Creates a new PLC address with specified parameters.
    /// </summary>
    /// <param name="dataBlock">Data block number (must be non-negative).</param>
    /// <param name="dataType">Data type character (X, B, W, or D).</param>
    /// <param name="offset">Byte offset (must be non-negative).</param>
    /// <param name="bitOffset">Bit offset (0-7, only for bit addresses).</param>
    public PlcAddress(int dataBlock, char dataType, int offset, int bitOffset = 0)
    {
        if (dataBlock < 0)
            throw new ArgumentException("Data block number cannot be negative.", nameof(dataBlock));

        if (offset < 0)
            throw new ArgumentException("Offset cannot be negative.", nameof(offset));

        dataType = char.ToUpperInvariant(dataType);
        if (dataType != 'X' && dataType != 'B' && dataType != 'W' && dataType != 'D')
            throw new ArgumentException("Data type must be X, B, W, or D.", nameof(dataType));

        if (dataType == 'X' && (bitOffset < 0 || bitOffset > 7))
            throw new ArgumentException("Bit offset must be between 0 and 7.", nameof(bitOffset));

        if (dataType != 'X' && bitOffset != 0)
            throw new ArgumentException("Bit offset is only valid for bit addresses (DataType='X').", nameof(bitOffset));

        DataBlock = dataBlock;
        DataType = dataType;
        Offset = offset;
        BitOffset = bitOffset;
    }

    /// <summary>
    /// Parses a string representation of a PLC address.
    /// </summary>
    /// <param name="address">Address string (e.g., "DB1.DBW0", "DB10.DBX5.3").</param>
    /// <returns>Parsed PlcAddress instance.</returns>
    /// <exception cref="ArgumentException">Thrown when address format is invalid.</exception>
    public static PlcAddress Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be null or empty.", nameof(address));

        // Handle bit addresses: DB1.DBX0.5
        var bitMatch = PlcAddressPatterns.BitAddressPattern().Match(address);
        if (bitMatch.Success)
        {
            var db = int.Parse(bitMatch.Groups["db"].Value);
            var offset = int.Parse(bitMatch.Groups["offset"].Value);
            var bit = int.Parse(bitMatch.Groups["bit"].Value);
            return new PlcAddress(db, 'X', offset, bit);
        }

        // Handle byte/word/dword addresses: DB1.DBW0
        var match = PlcAddressPatterns.AddressPattern().Match(address);
        if (!match.Success)
            throw new ArgumentException($"Invalid address format: '{address}'. Expected format: DB{{number}}.DB{{type}}{{offset}} (e.g., DB1.DBW0, DB10.DBX5.3).", nameof(address));

        var dataBlock = int.Parse(match.Groups["db"].Value);
        var dataType = char.ToUpperInvariant(match.Groups["type"].Value[0]);
        var byteOffset = int.Parse(match.Groups["offset"].Value);

        return new PlcAddress(dataBlock, dataType, byteOffset);
    }

    /// <summary>
    /// Attempts to parse a string representation of a PLC address.
    /// </summary>
    /// <param name="address">Address string to parse.</param>
    /// <param name="result">Parsed address if successful.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    public static bool TryParse(string address, out PlcAddress result)
    {
        try
        {
            result = Parse(address);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Returns the string representation of this address.
    /// </summary>
    public override string ToString()
    {
        if (DataType == 'X')
            return $"DB{DataBlock}.DBX{Offset}.{BitOffset}";

        return $"DB{DataBlock}.DB{DataType}{Offset}";
    }
}
