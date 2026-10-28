using System.Security.Cryptography;
using System.Text;

namespace Api.Endpoints;

/// <summary>Validates the <c>X-Webhook-Secret</c> header against <c>Webhooks:Secret</c> in constant time.</summary>
public static class WebhookSecret
{
    public const string HeaderName = "X-Webhook-Secret";

    public static bool IsValid(string? actual, IConfiguration configuration)
    {
        var expected = configuration["Webhooks:Secret"];
        if (string.IsNullOrEmpty(expected))
        {
            return false; // No secret configured: refuse rather than accept anything.
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual ?? string.Empty), Encoding.UTF8.GetBytes(expected));
    }
}
