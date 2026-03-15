namespace LinkedInMcp.Core.Services;

internal static class CsvExtensions
{
    public static IReadOnlyList<string> ToList(this string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string ToCsv(this IEnumerable<string> values)
        => string.Join(',', values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
