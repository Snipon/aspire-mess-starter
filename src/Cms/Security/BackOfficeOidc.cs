using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Umbraco.Cms.Api.Management.Security;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Infrastructure.Manifest;

namespace Cms.Security;

public sealed class BackOfficeOidcOptions
{
    public const string SectionName = "BackOfficeOidc";

    /// <summary>Issuer URL, e.g. <c>https://login.microsoftonline.com/{tenant}/v2.0</c> or a Keycloak realm.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Label of the sign-in button on the backoffice login page.</summary>
    public string DisplayName { get; set; } = "Single sign-on";

    /// <summary>
    /// Hides the local login form and redirects straight to the identity provider. Off by default: until a user with a
    /// mapped admin role exists in the identity provider, local login is the only way in.
    /// </summary>
    public bool DenyLocalLogin { get; set; }

    /// <summary>Claim (as issued in the ID token) that carries the user's app roles.</summary>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>
    /// Role claim value to comma-separated Umbraco user group aliases. Keys can be added or remapped in configuration.
    /// The groups named here are managed by the identity provider; other groups a user has are left alone.
    /// </summary>
    public Dictionary<string, string> RoleGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "admin,sensitiveData",
        ["editor"] = "editor",
    };

    /// <summary>
    /// Boolean claim that must be <c>true</c> before a sign-in is linked to an existing backoffice user by email
    /// (<c>email_verified</c> in standard OIDC; <c>xms_edov</c> as an optional claim in Entra ID). Without it, anyone who
    /// can set their own email at the provider could take over a backoffice user.
    /// </summary>
    public string EmailVerifiedClaim { get; set; } = "email_verified";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Optional backoffice sign-in through any OpenID Connect provider (Keycloak locally, Entra ID by configuration),
/// registered only when <c>BackOfficeOidc</c> is configured. Users are auto-linked by email (an enabled local user with
/// the same, verified email is linked, otherwise one is created), and on every sign-in their managed Umbraco groups are
/// set from the role claim; a user without a mapped role is refused. Refusing does not save the user, so revoking
/// every role at the provider leaves the user's groups (and any local password) in place: disable the backoffice user
/// in Umbraco as well.
/// </summary>
public sealed class BackOfficeOidcComposer : IComposer
{
    // Must match the authProvider manifest's forProviderName.
    internal static readonly string Scheme = BackOfficeAuthenticationBuilder.SchemeForBackOffice("Oidc");

    public void Compose(IUmbracoBuilder builder)
    {
        var options = builder.Config.GetSection(BackOfficeOidcOptions.SectionName).Get<BackOfficeOidcOptions>();
        if (options is not { IsConfigured: true })
        {
            return;
        }

        // The login button comes from an authProvider manifest, served only when the provider exists. Inserted first
        // because PackagingService resolves a single IPackageManifestReader (the last registered, App_Plugins).
        builder.Services.Insert(0, ServiceDescriptor.Singleton<IPackageManifestReader>(new BackOfficeOidcManifestReader(options)));

        builder.AddBackOfficeExternalLogins(logins => logins.AddBackOfficeLogin(
            auth => auth.AddOpenIdConnect(Scheme, options.DisplayName, oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = "code"; // with PKCE (default)
                oidc.CallbackPath = "/umbraco-signin-oidc";
                oidc.Scope.Add("email");
                // Inbound claim mapping stays on: email -> ClaimTypes.Email (needed for auto-linking), roles -> ClaimTypes.Role.
                oidc.TokenValidationParameters.NameClaimType = "name";
            }),
            provider =>
            {
                provider.DenyLocalLogin = options.DenyLocalLogin;
                provider.AutoLinkOptions = new ExternalSignInAutoLinkOptions(autoLinkExternalAccount: true, allowManualLinking: false)
                {
                    // Before linking or creating: throwing refuses it (no user is created).
                    OnAutoLinking = (user, login) =>
                    {
                        // Umbraco links an existing user found by email without its usual sign-in checks.
                        if (user.HasIdentity && !IsTrue(login.Principal, options.EmailVerifiedClaim))
                        {
                            throw new InvalidOperationException("The identity provider does not assert a verified email.");
                        }

                        if (user.HasIdentity && (!user.IsApproved || user.IsLockedOut))
                        {
                            throw new InvalidOperationException("The backoffice user is disabled or locked out.");
                        }

                        if (!Sync(user, login, options))
                        {
                            throw new InvalidOperationException("The identity provider grants this user no backoffice role.");
                        }
                    },
                    // Every sign-in; Umbraco saves the user afterwards.
                    OnExternalLogin = (user, login) => Sync(user, login, options),
                };
            }));
    }

    /// <summary>
    /// Sets the user's managed groups (those in <see cref="BackOfficeOidcOptions.RoleGroups"/>) to the ones mapped from the
    /// role claim, keeps other groups, and updates the name; false when no role maps.
    /// </summary>
    private static bool Sync(BackOfficeIdentityUser user, ExternalLoginInfo login, BackOfficeOidcOptions options)
    {
        var granted = Claims(login.Principal, options.RoleClaim)
            .SelectMany(c => Groups(options.RoleGroups.GetValueOrDefault(c.Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (granted.Count == 0)
        {
            return false;
        }

        // Roles changes are tracked through the collection (the setter is not), so edit it in place.
        var managed = options.RoleGroups.Values.SelectMany(Groups).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var role in user.Roles.Where(r => managed.Contains(r.RoleId) && !granted.Contains(r.RoleId)).ToList())
        {
            user.Roles.Remove(role);
        }

        foreach (var group in granted.Where(g => !user.Roles.Any(r => string.Equals(r.RoleId, g, StringComparison.OrdinalIgnoreCase))))
        {
            user.AddRole(group);
        }

        if (login.Principal.Identity?.Name is { Length: > 0 } name && name != user.Name)
        {
            user.Name = name;
        }

        return true;
    }

    /// <summary>Claims of the given type as issued, or as renamed by the default inbound claim mapping.</summary>
    private static IEnumerable<Claim> Claims(ClaimsPrincipal principal, string type)
    {
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.TryGetValue(type, out var mapped);
        return principal.Claims.Where(c => c.Type == type || c.Type == mapped);
    }

    private static bool IsTrue(ClaimsPrincipal principal, string type) =>
        Claims(principal, type).Any(c => bool.TryParse(c.Value, out var value) && value);

    private static string[] Groups(string? aliases) =>
        aliases?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}

internal sealed class BackOfficeOidcManifestReader(BackOfficeOidcOptions options) : IPackageManifestReader
{
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync() =>
        Task.FromResult<IEnumerable<PackageManifest>>(
        [
            new PackageManifest
            {
                Name = "Backoffice OIDC",
                AllowPublicAccess = true, // the login page loads only public manifests
                Extensions =
                [
                    new
                    {
                        type = "authProvider",
                        alias = "Headless.AuthProvider.Oidc",
                        name = "Backoffice OIDC",
                        forProviderName = BackOfficeOidcComposer.Scheme,
                        meta = new
                        {
                            label = options.DisplayName,
                            defaultView = new { icon = "icon-lock", color = "default", look = "primary" },
                            behavior = new { autoRedirect = options.DenyLocalLogin },
                            linking = new { allowManualLinking = false },
                        },
                    },
                ],
            },
        ]);
}
