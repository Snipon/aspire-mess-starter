using System.Security.Cryptography;
using System.Text;

namespace Api.Endpoints;

/// <summary>Validates shared-secret headers in constant time.</summary>
public static class SharedSecret
{
    /// <summary>Sent by the CMS webhook; compared with <c>Webhooks:Secret</c>.</summary>
    public const string WebhookHeader = "X-Webhook-Secret";

    /// <summary>Required for a manual reindex; compared with <c>Search:ReindexSecret</c>.</summary>
    /// <remarks>Kept apart from the webhook secret, which CMS Settings users can read in the webhook's headers.</remarks>
    public const string ReindexHeader = "X-Reindex-Secret";

    /// <summary>Sent by the website for staff preview; compared with <c>Preview:ApiKey</c>.</summary>
    public const string PreviewHeader = "X-Preview-Key";

    public static bool IsValid(string? actual, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return false; // No secret configured: refuse rather than accept anything.
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual ?? string.Empty), Encoding.UTF8.GetBytes(expected));
    }
}
