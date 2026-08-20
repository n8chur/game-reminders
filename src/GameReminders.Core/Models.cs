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
    /// What the launcher last reported about the game's files. Defaults to
    /// <see cref="Core.InstallState.Installed"/> so catalogs written before this
    /// field existed load unchanged.
    /// </summary>
    public InstallState InstallState { get; init; }
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];
}

public enum InstallState
{
    /// <summary>The launcher reports the game's files are on disk.</summary>
    Installed,

    /// <summary>
    /// The launcher knows the game but has not finished downloading it, so there are
    /// no executables to map yet and nothing for the user to review.
    /// </summary>
    Installing,

    /// <summary>
    /// The launcher no longer reports the game at all. Any executable mapping is kept
    /// so it resolves again if the game is reinstalled.
    /// </summary>
    NotInstalled
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
