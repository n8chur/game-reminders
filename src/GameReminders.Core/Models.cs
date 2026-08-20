using System.Text.Json.Serialization;

namespace GameReminders.Core;

public sealed record GameCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<GameDefinition> Games { get; init; } = [];
}

public sealed record GameDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Processes { get; init; } = [];
    public GameSource? Source { get; init; }
}

public sealed record GameSource
{
    public required string Type { get; init; }
    public string? AppId { get; init; }
    public bool RequiresExecutableReview { get; init; }

    /// <summary>
    /// The launcher reports the game as not yet fully installed, so it has no
    /// executables to map and must not be treated as needing user review.
    /// </summary>
    public bool InstallationPending { get; init; }
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];
}

public sealed record Reminder
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid Id { get; init; }
    public required string GameId { get; init; }
    public required string GameNameAtCreation { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }
}
