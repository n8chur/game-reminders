namespace GameReminders.Core;

public enum ReminderCreationStatus
{
    Created,
    EmptyGameId,
    GameNotFound,
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
        string selectedGameId,
        string message,
        Guid reminderId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedGameId);
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(selectedGameId))
        {
            return Failed(ReminderCreationStatus.EmptyGameId);
        }

        var game = catalog.Games.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, selectedGameId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            return Failed(ReminderCreationStatus.GameNotFound);
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

    private static ReminderCreationResult Failed(ReminderCreationStatus status) =>
        new() { Status = status };
}
