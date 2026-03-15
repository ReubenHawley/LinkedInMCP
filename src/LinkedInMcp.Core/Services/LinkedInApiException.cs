using LinkedInMcp.Core.Models;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInApiException(LinkedInErrorDetails error)
    : Exception($"{error.code}: {error.message}")
{
    public LinkedInErrorDetails Error { get; } = error;
}
