using System.Text;

namespace GameReminders.Core;

public static class NameNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLowerInvariant(character));
            }
        }

        return result.ToString();
    }

    public static string NormalizeProcessName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var filename = Path.GetFileName(value.Trim());
        return filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? filename[..^4].ToLowerInvariant()
            : filename.ToLowerInvariant();
    }

    public static bool IsExecutablePath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Contains('\\') || value.Contains('/');
    }

    public static string NormalizeExecutableIdentity(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim().Replace('/', '\\');
        while (trimmed.Contains("\\\\", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return IsExecutablePath(trimmed)
            ? trimmed.TrimEnd('\\').ToLowerInvariant()
            : NormalizeProcessName(trimmed);
    }

    public static bool ExecutablePathMatches(string configuredPath, string runningPath)
    {
        var configured = NormalizeExecutableIdentity(configuredPath);
        var running = NormalizeExecutableIdentity(runningPath);
        return string.Equals(configured, running, StringComparison.OrdinalIgnoreCase) ||
            (IsExecutablePath(configured) &&
                running.EndsWith($"\\{configured.TrimStart('\\')}", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ExecutableMatches(string configured, string observed) =>
        IsExecutablePath(configured)
            ? ExecutablePathMatches(configured, observed)
            : string.Equals(
                NormalizeProcessName(configured),
                NormalizeProcessName(observed),
                StringComparison.OrdinalIgnoreCase);

    public static bool ExecutableMappingsOverlap(string left, string right)
    {
        var normalizedLeft = NormalizeExecutableIdentity(left);
        var normalizedRight = NormalizeExecutableIdentity(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase) ||
            (IsExecutablePath(normalizedLeft) && IsExecutablePath(normalizedRight) &&
                (ExecutablePathMatches(normalizedLeft, normalizedRight) ||
                    ExecutablePathMatches(normalizedRight, normalizedLeft)));
    }
}
