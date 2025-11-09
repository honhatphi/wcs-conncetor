using System.Text.Json;
using System.Text.Json.Serialization;
using TQG.Automation.SDK.Models;

namespace TQG.Automation.SDK.Configuration;

/// <summary>
/// Root configuration object for loading PLC connections from JSON.
/// </summary>
public sealed class PlcGatewayConfiguration
{
    /// <summary>
    /// Gets or sets the collection of PLC connection configurations.
    /// </summary>
    [JsonPropertyName("plcConnections")]
    public List<PlcConnectionOptions> PlcConnections { get; set; } = [];

    /// <summary>
    /// Loads configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Loaded configuration.</returns>
    /// <exception cref="FileNotFoundException">Thrown when file is not found.</exception>
    /// <exception cref="JsonException">Thrown when JSON is invalid.</exception>
    public static async Task<PlcGatewayConfiguration> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Configuration file not found: {filePath}", filePath);

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads configuration from a JSON string.
    /// </summary>
    /// <param name="json">JSON string containing configuration.</param>
    /// <returns>Loaded configuration.</returns>
    /// <exception cref="JsonException">Thrown when JSON is invalid.</exception>
    public static PlcGatewayConfiguration LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };

        var config = JsonSerializer.Deserialize<PlcGatewayConfiguration>(json, options) 
            ?? throw new JsonException("Failed to deserialize configuration.");

        return config;
    }

    /// <summary>
    /// Saves the configuration to a JSON file.
    /// </summary>
    /// <param name="filePath">Path to save the JSON file.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SaveToFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var json = ToJson();
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    /// Converts the configuration to JSON string.
    /// </summary>
    /// <returns>JSON representation of the configuration.</returns>
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Validates all connection configurations.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (PlcConnections == null || PlcConnections.Count == 0)
            throw new ArgumentException("Configuration must contain at least one PLC connection.");

        foreach (var connection in PlcConnections)
        {
            connection.Validate();
        }

        // Check for duplicate device IDs
        var duplicates = PlcConnections
            .GroupBy(c => c.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new ArgumentException($"Duplicate device IDs found: {string.Join(", ", duplicates)}");
    }
}
