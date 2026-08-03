namespace LocalDesktopStore.Services;

public static class PublisherPinParser
{
    public static bool TryNormalize(
        string? repository,
        string? thumbprint,
        out string normalizedRepository,
        out string normalizedThumbprint,
        out string error)
    {
        normalizedRepository = string.Empty;
        normalizedThumbprint = string.Empty;

        var parts = repository?.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 }
            || parts.Any(part => string.IsNullOrWhiteSpace(part)
                || part.Any(char.IsWhiteSpace)
                || part.Contains('\\')
                || part.Contains('?')
                || part.Contains('#')
                || part.Contains('=')))
        {
            error = "Repository pins must use the owner/repo form.";
            return false;
        }

        var compactThumbprint = new string((thumbprint ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && character is not ':' and not '-')
            .ToArray());
        if (compactThumbprint.Length != 40 || compactThumbprint.Any(character => !Uri.IsHexDigit(character)))
        {
            error = "Publisher pins must be a 40-character SHA-1 thumbprint.";
            return false;
        }

        normalizedRepository = $"{parts[0]}/{parts[1]}";
        normalizedThumbprint = NormalizeThumbprint(compactThumbprint);
        error = string.Empty;
        return true;
    }

    public static bool TryParseLine(
        string line,
        out string repository,
        out string thumbprint,
        out string error)
    {
        repository = string.Empty;
        thumbprint = string.Empty;
        var separator = line.IndexOf('=');
        if (separator <= 0 || separator == line.Length - 1)
        {
            error = "Each publisher pin must use owner/repo=THUMBPRINT.";
            return false;
        }

        return TryNormalize(line[..separator], line[(separator + 1)..], out repository, out thumbprint, out error);
    }

    public static bool TryParseLines(
        string? text,
        out Dictionary<string, string> pins,
        out string error)
    {
        pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (!TryParseLine(line, out var repository, out var thumbprint, out error))
            {
                pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return false;
            }

            if (!pins.TryAdd(repository, thumbprint))
            {
                error = $"Publisher pin '{repository}' is listed more than once.";
                pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static Dictionary<string, string> Sanitize(IEnumerable<KeyValuePair<string, string>>? pins)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pins ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            if (TryNormalize(pair.Key, pair.Value, out var repository, out var thumbprint, out _))
                result[repository] = thumbprint;
        }
        return result;
    }

    public static string Format(IEnumerable<KeyValuePair<string, string>>? pins)
        => string.Join(
            Environment.NewLine,
            Sanitize(pins)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}"));

    private static string NormalizeThumbprint(string thumbprint)
        => new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
