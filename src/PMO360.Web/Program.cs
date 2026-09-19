using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PMO360.Application.Abstractions;
using PMO360.Infrastructure.Startup;
using PMO360.Web.Components;
using PMO360.Web.Endpoints;
using PMO360.Web.Identity;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Authentication — single sign-on against Microsoft Entra ID (section 6).
// The portal asks for group membership in the token; RoleClaimsTransformation turns those
// groups into the BRD's roles.
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);

        // Groups arrive as object ids; the name claim is what the portal shows in the corner.
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });

builder.Services.AddAuthorization(options => options.AddPmoPolicies());
builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, RoleClaimsTransformation>();
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

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

// Behind Azure App Service the app sees HTTP; without this the redirect URI it builds for
// Entra ID would be http:// and the sign-in would be rejected.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
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
