using LinkedInMcp.Core.Data;

namespace LinkedInMcp.Core.Services;

internal static class LinkedInConnectionExtensions
{
    public static IReadOnlyList<string> GetEffectiveScopes(this LinkedInConnection connection)
        => string.IsNullOrWhiteSpace(connection.ValidatedScopeCsv)
            ? connection.ScopeCsv.ToList()
            : connection.ValidatedScopeCsv.ToList();

    public static string GetMemberUrn(this LinkedInConnection connection)
        => string.IsNullOrWhiteSpace(connection.MemberUrn)
            ? $"urn:li:person:{connection.SubjectKey}"
            : connection.MemberUrn;
}
