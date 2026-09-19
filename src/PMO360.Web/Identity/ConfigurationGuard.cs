using PMO360.Application.Options;
using PMO360.Infrastructure.Startup;

namespace PMO360.Web.Identity;

/// <summary>
/// Checks at startup that the settings the portal cannot run without have real values.
///
/// Without this, a placeholder left in <c>appsettings.json</c> surfaces as whatever the service
/// it was passed to happens to do with it — and Entra ID's answer to a tenant id of
/// "&lt;tenant-guid&gt;" is an HTML error page, which the OpenID Connect handler then tries to
/// parse as JSON. The result is a 200-line stack trace full of minified login script and no
/// mention of the actual problem.
///
/// So the portal refuses to start instead, naming the setting and how to set it. Same reasoning
/// as the check that compares the database's controlled value lists with the application's enums.
/// </summary>
public static class ConfigurationGuard
{
    /// <summary>Anything still carrying the shape of a placeholder from the committed settings file.</summary>
    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('<') || value.Contains('>');

    public static void Validate(IConfiguration configuration, IHostEnvironment environment, SsoOptions sso)
    {
        var problems = new List<string>();

        // An unauthenticated portal must not be something you get to by forgetting a setting,
        // so saying so outside Development has to be deliberate and separate.
        if (!sso.EnableSso && !environment.IsDevelopment() && !sso.AllowSsoDisabledOutsideDevelopment)
        {
            problems.Add(
                $"Authentication:EnableSso is false in the {environment.EnvironmentName} environment. "
                + "With it off, everyone who can reach the site is signed in automatically and the "
                + "portal has no identity at all. Turn it back on, or set "
                + "Authentication:AllowSsoDisabledOutsideDevelopment to true to confirm that is "
                + "what you want.");
        }

        var connectionString = configuration.GetConnectionString("PmoDatabase");

        // The Entra ID settings only matter when the portal is actually signing people in.
        if (sso.EnableSso)
        {
            ValidateEntraId(configuration, problems);
        }

        if (IsPlaceholder(connectionString))
        {
            problems.Add(
                "ConnectionStrings:PmoDatabase is not set, or still carries the placeholder from "
                + "appsettings.json.");
        }

        if (problems.Count == 0)
        {
            return;
        }

        var howToSet = environment.IsDevelopment()
            ? "Set them with user secrets, which stay out of the repository:" + Environment.NewLine
              + "    cd src/PMO360.Web" + Environment.NewLine
              + "    dotnet user-secrets set \"AzureAd:TenantId\" \"<the tenant guid>\"" + Environment.NewLine
              + "    dotnet user-secrets set \"AzureAd:ClientId\" \"<the client guid>\"" + Environment.NewLine
              + "    dotnet user-secrets set \"ConnectionStrings:PmoDatabase\" \"Server=UATWEB01;...\""
            : "Set them as App Service application settings, using a double underscore for the "
              + "separator: AzureAd__TenantId, AzureAd__ClientId, ConnectionStrings__PmoDatabase.";

        throw new StartupFailureException(
            "PMO360 cannot start: " + problems.Count + " setting(s) are missing or still hold a placeholder."
            + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            + Environment.NewLine + Environment.NewLine
            + howToSet
            + Environment.NewLine + Environment.NewLine
            + "See docs/03-deployment.md for the app registration itself.");
    }

    private static void ValidateEntraId(IConfiguration configuration, List<string> problems)
    {
        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];

        // "common" and "organizations" are valid tenant values for a multi-tenant registration;
        // anything else has to be the tenant's own guid.
        var tenantIsWellKnown = string.Equals(tenantId, "common", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(tenantId, "organizations", StringComparison.OrdinalIgnoreCase);

        if (IsPlaceholder(tenantId) || (!tenantIsWellKnown && !Guid.TryParse(tenantId, out _)))
        {
            problems.Add(
                $"AzureAd:TenantId is '{tenantId ?? "(not set)"}'. It must be the directory (tenant) ID "
                + "from the app registration's Overview page - a GUID. "
                + "To run without signing in at all, set Authentication:EnableSso to false instead.");
        }

        if (IsPlaceholder(clientId) || !Guid.TryParse(clientId, out _))
        {
            problems.Add(
                $"AzureAd:ClientId is '{clientId ?? "(not set)"}'. It must be the application (client) ID "
                + "from the app registration's Overview page - a GUID.");
        }
    }

}
