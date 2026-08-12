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
        if (IsEmptyCatalogPlaceholder(json))
        {
            return new GameCatalog { UpdatedAt = DateTimeOffset.UnixEpoch };
        }

        var catalog = JsonSerializer.Deserialize<GameCatalog>(json, Options)
            ?? throw new InvalidDataException("games.json contained no catalog.");

        ValidateCatalog(catalog);
        return catalog;
    }

    internal static bool IsEmptyCatalogPlaceholder(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
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

    public static AliasRequest ReadAliasRequest(string json, string? sourcePath = null)
    {
        var request = JsonSerializer.Deserialize<AliasRequest>(json, Options)
            ?? throw new InvalidDataException("Alias request file contained no request.");

        ValidateAliasRequest(request);
        return request with { SourcePath = sourcePath };
    }

    public static string WriteAliasRequest(AliasRequest request)
    {
        ValidateAliasRequest(request);
        return JsonSerializer.Serialize(request, Options);
    }

    private static void ValidateCatalog(GameCatalog catalog)
    {
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported games.json schema version {catalog.SchemaVersion}.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processOwners = new List<(string Process, string OwnerId)>();
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

            if (game.Aliases is null)
            {
                throw new InvalidDataException($"Game '{game.Id}' requires an aliases collection.");
            }

            foreach (var alias in game.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    throw new InvalidDataException($"Game '{game.Id}' contains an empty alias.");
                }
            }

            foreach (var process in game.Processes)
            {
                if (string.IsNullOrWhiteSpace(process))
                {
                    throw new InvalidDataException($"Game '{game.Id}' contains an empty process name.");
                }

                var normalizedProcess = NameNormalizer.NormalizeExecutableIdentity(process);
                if (string.IsNullOrWhiteSpace(normalizedProcess))
                {
                    throw new InvalidDataException($"Game '{game.Id}' contains an empty process name.");
                }

                var conflict = processOwners.FirstOrDefault(item =>
                    !string.Equals(item.OwnerId, game.Id, StringComparison.OrdinalIgnoreCase) &&
                    NameNormalizer.ExecutableMappingsOverlap(item.Process, process));
                if (conflict != default)
                {
                    throw new InvalidDataException(
                        $"Process '{process}' is assigned to both '{conflict.OwnerId}' and '{game.Id}'.");
                }

                processOwners.Add((process, game.Id));
            }

            if (game.Source is not null && game.Source.ExecutableCandidates is null)
            {
                throw new InvalidDataException($"Game '{game.Id}' requires an executableCandidates collection.");
            }

            foreach (var candidate in game.Source?.ExecutableCandidates ?? [])
            {
                if (string.IsNullOrWhiteSpace(candidate) ||
                    string.IsNullOrWhiteSpace(NameNormalizer.NormalizeExecutableIdentity(candidate)))
                {
                    throw new InvalidDataException($"Game '{game.Id}' contains an empty executable candidate.");
                }
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

    private static void ValidateAliasRequest(AliasRequest request)
    {
        if (request.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported alias request schema version {request.SchemaVersion}.");
        }

        if (request.Id == Guid.Empty || string.IsNullOrWhiteSpace(request.GameId) ||
            string.IsNullOrWhiteSpace(request.Alias) || request.CreatedAt == default)
        {
            throw new InvalidDataException(
                "An alias request requires a non-empty id, gameId, alias, and createdAt.");
        }
    }
}
