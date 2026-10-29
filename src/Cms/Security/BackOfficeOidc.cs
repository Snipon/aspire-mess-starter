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

    /// <summary>Role claim value to Umbraco user group alias. Keys can be added or remapped in configuration.</summary>
    public Dictionary<string, string> RoleGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "admin",
        ["editor"] = "editor",
    };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Optional backoffice sign-in through any OpenID Connect provider (Keycloak locally, Entra ID in Azure), registered only
/// when <c>BackOfficeOidc</c> is configured. Users are auto-linked by email (a local user with the same email is linked,
/// otherwise one is created), and on every sign-in their Umbraco groups are replaced by the groups mapped from the role
/// claim; a user without a mapped role is refused.
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

    /// <summary>Replaces the user's groups with the mapped roles and updates the name; false when no role maps.</summary>
    private static bool Sync(BackOfficeIdentityUser user, ExternalLoginInfo login, BackOfficeOidcOptions options)
    {
        var roleClaimTypes = new HashSet<string>(StringComparer.Ordinal) { options.RoleClaim };
        if (JsonWebTokenHandler.DefaultInboundClaimTypeMap.TryGetValue(options.RoleClaim, out var mapped))
        {
            roleClaimTypes.Add(mapped);
        }

        var groups = login.Principal.Claims
            .Where(c => roleClaimTypes.Contains(c.Type))
            .Select(c => options.RoleGroups.GetValueOrDefault(c.Value))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0)
        {
            return false;
        }

        // Roles has no dirty tracking of its own; Clear/AddRole mark the user's groups as changed.
        user.Roles.Clear();
        foreach (var group in groups)
        {
            user.AddRole(group);
        }

        if (login.Principal.Identity?.Name is { Length: > 0 } name && name != user.Name)
        {
            user.Name = name;
        }

        return true;
    }
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
