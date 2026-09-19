using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;
using PMO360.Infrastructure.Startup;
using PMO360.Web.Components;
using PMO360.Web.Endpoints;
using PMO360.Web.Identity;

// Startup failures are printed as a message, not as a stack trace: the person reading them needs
// to know which setting to change, and no frame of the stack tells them that.
try
{
    Run(args);
}
catch (StartupFailureException failure)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(new string('-', 78));
    Console.Error.WriteLine(failure.Message);
    Console.Error.WriteLine(new string('-', 78));
    Console.Error.WriteLine();
    Environment.Exit(1);
}

static void Run(string[] args)
{
var builder = WebApplication.CreateBuilder(args);

var ssoOptions = builder.Configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>() ?? new SsoOptions();
builder.Services.Configure<SsoOptions>(builder.Configuration.GetSection(SsoOptions.SectionName));

// Fail here, with a message that names the setting, rather than somewhere deep inside a library
// that was handed a placeholder. See ConfigurationGuard for why this is worth doing.
ConfigurationGuard.Validate(builder.Configuration, builder.Environment, ssoOptions);

// ---------------------------------------------------------------------------
// Authentication.
//
// Normally: single sign-on against Microsoft Entra ID (BRD section 6), with the BRD's roles
// resolved from the security groups in the token.
//
// With Authentication:EnableSso set to false: nobody signs in, everyone is the configured local
// user, and the portal opens on the dashboard. For development and for demonstrating the portal
// before the app registration exists — never for real use, which is what the guard above and the
// banner on every page are for.
// ---------------------------------------------------------------------------
if (ssoOptions.EnableSso)
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.Bind("AzureAd", options);

            // Groups arrive as object ids; the name claim is what the portal shows in the corner.
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
        });

    builder.Services.AddScoped<IClaimsTransformation, RoleClaimsTransformation>();

    builder.Services.AddControllersWithViews()
        .AddMicrosoftIdentityUI();
}
else
{
    builder.Services
        .AddAuthentication(LocalAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LocalAuthenticationHandler>(
            LocalAuthenticationHandler.SchemeName, _ => { });

    builder.Services.AddControllersWithViews();
}

builder.Services.AddAuthorization(options => options.AddPmoPolicies());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ---------------------------------------------------------------------------
// The data layer (stored procedures only), Azure Blob Storage, Microsoft Graph and the
// scheduled notifications.
// ---------------------------------------------------------------------------
builder.Services.AddPmoInfrastructure(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Behind Azure App Service the app sees HTTP; without this the redirect URI it builds for
// Entra ID would be http:// and the sign-in would be rejected.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (!ssoOptions.EnableSso)
{
    app.Logger.LogWarning(
        "Single sign-on is DISABLED. Every visitor is signed in automatically as '{User}' with "
        + "roles {Roles}. Set Authentication:EnableSso to true before this portal carries real data.",
        ssoOptions.LocalUser.DisplayName, string.Join(", ", ssoOptions.LocalUser.EffectiveRoles));
}

app.UseForwardedHeaders();

// Always the portal's own error page, in every environment. A user must never be shown a stack
// trace, and the one that ASP.NET produces for a failed Entra ID discovery is 200 lines of
// minified Microsoft login script — unreadable, and it leaks how the application is built.
// The detail goes to the log, against a reference the user can quote.
app.UseExceptionHandler("/error", createScopeForErrors: true);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    // The portal renders only its own content. A tight policy is cheap here because there is no
    // third-party script in the application at all.
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; "
        + "script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://login.microsoftonline.com";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPortalEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
}
