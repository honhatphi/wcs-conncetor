using System.Text.Json;
using TQG.Automation.SDK.Models;

namespace TQG.Automation.SDK.Configuration;

/// <summary>
/// Defines the warehouse storage layout configuration.
/// Controls which storage locations are valid and active.
/// </summary>
public sealed class WarehouseLayout
{
    /// <summary>
    /// Block configurations defining valid ranges for each block type.
    /// </summary>
    public required List<BlockConfiguration> Blocks { get; init; }

    /// <summary>
    /// List of specific locations that are disabled/deactivated.
    /// These locations cannot be used for storage.
    /// </summary>
    public List<LocationPattern> DisabledLocations { get; init; } = [];

    /// <summary>
    /// Validates the entire layout configuration.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Blocks, nameof(Blocks));

        if (Blocks.Count == 0)
            throw new ArgumentException("At least one block configuration is required.", nameof(Blocks));

        foreach (var block in Blocks)
        {
            block.Validate();
        }
    }

    /// <summary>
    /// Checks if a location is valid according to this layout.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateLocation(Location location, string locationName)
    {
        // Find matching block configuration
        var blockConfig = Blocks.FirstOrDefault(b => b.BlockNumber == location.Block);
        if (blockConfig == null)
        {
            var validBlocks = string.Join(", ", Blocks.Select(b => b.BlockNumber));
            return (false, $"{locationName}.Block must be one of: {validBlocks}. Current: {location.Block}");
        }

        // Check floor range for this block
        if (location.Floor < 1 || location.Floor > blockConfig.MaxFloor)
            return (false, $"{locationName}.Floor must be between 1 and {blockConfig.MaxFloor} for Block {location.Block}. Current: {location.Floor}");

        // Check rail range for this block
        if (location.Rail < 1 || location.Rail > blockConfig.MaxRail)
            return (false, $"{locationName}.Rail must be between 1 and {blockConfig.MaxRail} for Block {location.Block}. Current: {location.Rail}");

        // Check depth range for this block
        if (location.Depth < 1 || location.Depth > blockConfig.MaxDepth)
            return (false, $"{locationName}.Depth must be between 1 and {blockConfig.MaxDepth} for Block {location.Block}. Current: {location.Depth}");

        // Check if location is disabled
        if (IsLocationDisabled(location))
        {
            return (false, $"{locationName} {location} is disabled and cannot be used for storage.");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if a specific location is disabled.
    /// </summary>
    private bool IsLocationDisabled(Location location)
    {
        foreach (var pattern in DisabledLocations)
        {
            if (pattern.Matches(location))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Loads warehouse layout from JSON string.
    /// </summary>
    public static WarehouseLayout LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var layout = JsonSerializer.Deserialize<WarehouseLayout>(json, options)
            ?? throw new ArgumentException("Failed to deserialize warehouse layout configuration.");

        layout.Validate();
        return layout;
    }

    /// <summary>
    /// Creates a default warehouse layout configuration.
    /// Block 3: Floor 1-7, Rail 1-24, Depth 1-8
    /// Block 5: Floor 1-7, Rail 1-24, Depth 1-3
    /// </summary>
    public static WarehouseLayout CreateDefault()
    {
        return new WarehouseLayout
        {
            Blocks =
            [
                new BlockConfiguration 
                { 
                    BlockNumber = 3, 
                    MaxFloor = 7, 
                    MaxRail = 24, 
                    MaxDepth = 8 
                },
                new BlockConfiguration 
                { 
                    BlockNumber = 5, 
                    MaxFloor = 7, 
                    MaxRail = 24, 
                    MaxDepth = 3 
                }
            ],
            DisabledLocations = []
        };
    }
}

/// <summary>
/// Defines configuration for a specific block type.
/// Each block has its own valid ranges for floor, rail, and depth.
/// </summary>
public sealed class BlockConfiguration
{
    /// <summary>
    /// Block number (e.g., 3 or 5).
    /// </summary>
    public required int BlockNumber { get; init; }

    /// <summary>
    /// Maximum floor number for this block (min is always 1).
    /// </summary>
    public required int MaxFloor { get; init; }

    /// <summary>
    /// Maximum rail number for this block (min is always 1).
    /// </summary>
    public required int MaxRail { get; init; }

    /// <summary>
    /// Maximum depth for this block (min is always 1).
    /// </summary>
    public required int MaxDepth { get; init; }

    public void Validate()
    {
        if (BlockNumber < 1)
            throw new ArgumentException($"Block number must be at least 1. Current: {BlockNumber}");
        if (MaxFloor < 1)
            throw new ArgumentException($"MaxFloor must be at least 1 for Block {BlockNumber}. Current: {MaxFloor}");
        if (MaxRail < 1)
            throw new ArgumentException($"MaxRail must be at least 1 for Block {BlockNumber}. Current: {MaxRail}");
        if (MaxDepth < 1)
            throw new ArgumentException($"MaxDepth must be at least 1 for Block {BlockNumber}. Current: {MaxDepth}");
    }
}

/// <summary>
/// Defines a pattern for matching locations to disable.
/// Supports wildcards (null) for flexible matching.
/// </summary>
public sealed class LocationPattern
{
    /// <summary>
    /// Floor number or null for wildcard (matches any floor).
    /// </summary>
    public int? Floor { get; init; }

    /// <summary>
    /// Rail number or null for wildcard (matches any rail).
    /// </summary>
    public int? Rail { get; init; }

    /// <summary>
    /// Block number or null for wildcard (matches any block).
    /// </summary>
    public int? Block { get; init; }

    /// <summary>
    /// Depth number or null for wildcard (matches any depth).
    /// </summary>
    public int? Depth { get; init; }

    /// <summary>
    /// Checks if this pattern matches a given location.
    /// </summary>
    public bool Matches(Location location)
    {
        if (Floor.HasValue && Floor.Value != location.Floor)
            return false;

        if (Rail.HasValue && Rail.Value != location.Rail)
            return false;

        if (Block.HasValue && Block.Value != location.Block)
            return false;

        if (Depth.HasValue && Depth.Value != location.Depth)
            return false;

        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>
        {
            Floor.HasValue ? $"F{Floor}" : "F*",
            Rail.HasValue ? $"R{Rail}" : "R*",
            Block.HasValue ? $"B{Block}" : "B*",
            Depth.HasValue ? $"D{Depth}" : "D*"
        };

        return string.Join("-", parts);
    }
}
