namespace TQG.Automation.SDK.Shared;

/// <summary>
/// Represents a location in the warehouse with detailed coordinate information.
/// </summary>
public sealed record Location
{
    /// <summary>
    /// Floor level (e.g., 1, 2, 3).
    /// </summary>
    public required int Floor { get; init; }

    /// <summary>
    /// Rail identifier within the floor.
    /// </summary>
    public required int Rail { get; init; }

    /// <summary>
    /// Block/column number along the rail.
    /// </summary>
    public required int Block { get; init; }

    /// <summary>
    /// Depth level within the block (how deep into the storage).
    /// </summary>
    public int Depth { get; init; } = 1;

    /// <summary>
    /// Returns a formatted string representation of the location.
    /// Format: F{Floor}R{Rail}B{Block}D{Depth}
    /// </summary>
    public override string ToString()
    {
        return $"F{Floor}R{Rail}B{Block}D{Depth}";
    }

    /// <summary>
    /// Parses a location string in format "F{Floor}R{Rail}B{Block}D{Depth}".
    /// </summary>
    /// <param name="locationString">Location string to parse.</param>
    /// <returns>Parsed Location object.</returns>
    /// <exception cref="ArgumentException">Thrown when format is invalid.</exception>
    public static Location Parse(string locationString)
    {
        if (string.IsNullOrWhiteSpace(locationString))
            throw new ArgumentException("Location string cannot be null or empty.", nameof(locationString));

        try
        {
            var parts = locationString.ToUpperInvariant()
                .Replace("F", "|")
                .Replace("R", "|")
                .Replace("B", "|")
                .Replace("D", "|")
                .Split('|', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4)
                throw new ArgumentException($"Invalid location format: {locationString}. Expected format: F{{Floor}}R{{Rail}}B{{Block}}D{{Depth}}");

            return new Location
            {
                Floor = int.Parse(parts[0]),
                Rail = int.Parse(parts[1]),
                Block = int.Parse(parts[2]),
                Depth = int.Parse(parts[3])
            };
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"Invalid location format: {locationString}. Expected format: F{{Floor}}R{{Rail}}B{{Block}}D{{Depth}}", nameof(locationString), ex);
        }
    }

    /// <summary>
    /// Tries to parse a location string. Returns true if successful.
    /// </summary>
    public static bool TryParse(string locationString, out Location? location)
    {
        try
        {
            location = Parse(locationString);
            return true;
        }
        catch
        {
            location = null;
            return false;
        }
    }
}
