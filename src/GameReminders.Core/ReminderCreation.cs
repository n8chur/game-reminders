namespace GameReminders.Core;

public enum ReminderCreationStatus
{
    Created,
    EmptyGameName,
    UnknownGame,
    AmbiguousGame,
    EmptyMessage
}

public sealed record ReminderCreationResult
{
    public required ReminderCreationStatus Status { get; init; }
    public Reminder? Reminder { get; init; }
}

/// <summary>
/// Executable reference for the matching and reminder-construction behavior
/// implemented by the iPhone Shortcut.
/// </summary>
public static class ReminderCreation
{
    public static ReminderCreationResult Create(
        GameCatalog catalog,
        string requestedGameName,
        string message,
        Guid reminderId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedGameName);
        ArgumentNullException.ThrowIfNull(message);

        var requestedKey = NameNormalizer.Normalize(requestedGameName);
        if (string.IsNullOrEmpty(requestedKey))
        {
            return Failed(ReminderCreationStatus.EmptyGameName);
        }

        var matches = catalog.Games
            .Where(game => Matches(game, requestedKey))
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
        {
            return Failed(ReminderCreationStatus.UnknownGame);
        }

        if (matches.Length > 1)
        {
            return Failed(ReminderCreationStatus.AmbiguousGame);
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Failed(ReminderCreationStatus.EmptyMessage);
        }

        if (reminderId == Guid.Empty)
        {
            throw new ArgumentException("A generated reminder id cannot be empty.", nameof(reminderId));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("A generated creation timestamp cannot be empty.", nameof(createdAt));
        }

        var game = matches[0];
        return new ReminderCreationResult
        {
            Status = ReminderCreationStatus.Created,
            Reminder = new Reminder
            {
                Id = reminderId,
                GameId = game.Id,
                GameNameAtCreation = game.Name,
                Message = message,
                CreatedAt = createdAt
            }
        };
    }

    private static bool Matches(GameDefinition game, string requestedKey) =>
        NameNormalizer.Normalize(game.Name) == requestedKey ||
        game.Aliases.Any(alias => NameNormalizer.Normalize(alias) == requestedKey);

    private static ReminderCreationResult Failed(ReminderCreationStatus status) =>
        new() { Status = status };
}
