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
        var filename = Path.GetFileName(value.Trim());
        return filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? filename[..^4].ToLowerInvariant()
            : filename.ToLowerInvariant();
    }
}

