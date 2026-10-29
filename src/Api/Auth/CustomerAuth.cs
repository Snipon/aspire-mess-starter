using System.Security.Claims;
using Api.Commerce;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Api.Auth;

/// <summary>
/// Shop customers sign in at an OpenID Connect provider (the web runs the code flow) and the web sends their access
/// token as a bearer token. Validation is generic OIDC: issuer and signing keys from the authority's discovery document,
/// audience <see cref="CustomerAuthOptions.Audience"/>. Off (every request is anonymous) when no authority is set.
/// </summary>
public static class CustomerAuth
{
    public static IHostApplicationBuilder AddCustomerAuth(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(CustomerAuthOptions.SectionName).Get<CustomerAuthOptions>() ?? new();
        builder.Services.AddSingleton(options);
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            return builder;
        }

        // Fail closed: without an audience any token the issuer signs (for another app of the tenant) would pass.
        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException($"{CustomerAuthOptions.SectionName}:Audience is required when {CustomerAuthOptions.SectionName}:Authority is set.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = options.Authority;
            o.Audience = options.Audience;
            o.MapInboundClaims = false; // keep "sub", "email" as issued
        });
        return builder;
    }

    /// <summary>Authenticates bearer tokens when customer auth is on; a no-op otherwise.</summary>
    public static IApplicationBuilder UseCustomerAuth(this WebApplication app) =>
        string.IsNullOrWhiteSpace(app.Services.GetRequiredService<CustomerAuthOptions>().Authority) ? app : app.UseAuthentication();

    /// <summary>The signed-in customer, or null for an anonymous request.</summary>
    public static CustomerIdentity? GetCustomer(this HttpContext http)
    {
        var subjectClaim = http.RequestServices.GetRequiredService<CustomerAuthOptions>().SubjectClaim;
        return http.User.Identity?.IsAuthenticated == true && http.User.FindFirstValue(subjectClaim) is { Length: > 0 } subject
            ? new CustomerIdentity(subject, http.User.FindFirstValue("email"))
            : null;
    }

    /// <summary>
    /// A bearer token was sent but yields no customer: invalid, expired, or valid without the subject claim (a provider
    /// misconfiguration). The caller should sign in again rather than continue as a guest. Always false when customer
    /// auth is off (the header is ignored).
    /// </summary>
    public static bool HasRejectedToken(this HttpContext http) =>
        !string.IsNullOrWhiteSpace(http.RequestServices.GetRequiredService<CustomerAuthOptions>().Authority)
        && http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        && http.GetCustomer() is null;
}

public sealed class CustomerAuthOptions
{
    public const string SectionName = "CustomerAuth";

    /// <summary>Issuer URL of the customer realm / tenant (e.g. <c>https://localhost:8080/realms/shop</c>); empty = off.</summary>
    public string? Authority { get; set; }

    /// <summary>Required <c>aud</c> of access tokens (e.g. <c>shop-api</c>, or the API app's client id in Entra); required with an authority.</summary>
    public string? Audience { get; set; }

    /// <summary>Claim that identifies the customer. <c>sub</c>; <c>oid</c> in Entra ID, whose <c>sub</c> differs per app.</summary>
    public string SubjectClaim { get; set; } = "sub";
}
