using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameReminders.Core;

public static class JsonProtocol
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static GameCatalog ReadCatalog(string json)
    {
        var catalog = JsonSerializer.Deserialize<GameCatalog>(json, Options)
            ?? throw new InvalidDataException("games.json contained no catalog.");

        ValidateCatalog(catalog);
        return catalog;
    }

    public static Reminder ReadReminder(string json, string? sourcePath = null)
    {
        var reminder = JsonSerializer.Deserialize<Reminder>(json, Options)
            ?? throw new InvalidDataException("Reminder file contained no reminder.");

        ValidateReminder(reminder);
        return reminder with { SourcePath = sourcePath };
    }

    public static string WriteCatalog(GameCatalog catalog)
    {
        ValidateCatalog(catalog);
        return JsonSerializer.Serialize(catalog, Options);
    }

    public static string WriteReminder(Reminder reminder)
    {
        ValidateReminder(reminder);
        return JsonSerializer.Serialize(reminder, Options);
    }

    private static void ValidateCatalog(GameCatalog catalog)
    {
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported games.json schema version {catalog.SchemaVersion}.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (catalog.Games is null)
        {
            throw new InvalidDataException("games.json requires a games collection.");
        }

        foreach (var game in catalog.Games)
        {
            if (game is null)
            {
                throw new InvalidDataException("games.json cannot contain a null game.");
            }

            if (string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.Name))
            {
                throw new InvalidDataException("Every game requires a non-empty id and name.");
            }

            if (!ids.Add(game.Id))
            {
                throw new InvalidDataException($"Duplicate game id '{game.Id}'.");
            }

            if (game.Processes is null)
            {
                throw new InvalidDataException($"Game '{game.Id}' requires a processes collection.");
            }

            foreach (var process in game.Processes)
            {
                if (string.IsNullOrWhiteSpace(process))
                {
                    throw new InvalidDataException($"Game '{game.Id}' contains an empty process name.");
                }

                var normalizedProcess = NameNormalizer.NormalizeProcessName(process);

                if (processOwners.TryGetValue(normalizedProcess, out var ownerId) &&
                    !string.Equals(ownerId, game.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Process '{process}' is assigned to both '{ownerId}' and '{game.Id}'.");
                }

                processOwners[normalizedProcess] = game.Id;
            }
        }
    }

    private static void ValidateReminder(Reminder reminder)
    {
        if (reminder.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported reminder schema version {reminder.SchemaVersion}.");
        }

        if (reminder.Id == Guid.Empty || string.IsNullOrWhiteSpace(reminder.GameId) ||
            string.IsNullOrWhiteSpace(reminder.GameNameAtCreation) ||
            string.IsNullOrWhiteSpace(reminder.Message) || reminder.CreatedAt == default)
        {
            throw new InvalidDataException(
                "A reminder requires a non-empty id, gameId, gameNameAtCreation, message, and createdAt.");
        }
    }
}
