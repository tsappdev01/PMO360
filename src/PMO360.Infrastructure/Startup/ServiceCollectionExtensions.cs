using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using PMO360.Application.Abstractions;
using PMO360.Application.Options;
using PMO360.Application.Services;
using PMO360.Domain.Abstractions;
using PMO360.Infrastructure.Data;
using PMO360.Infrastructure.Documents;
using PMO360.Infrastructure.Email;
using PMO360.Infrastructure.Scheduling;
using PMO360.Infrastructure.Services;

namespace PMO360.Infrastructure.Startup;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the data layer, the Azure integrations and the application services.
    ///
    /// Everything that talks to Azure — SQL, Blob Storage, Graph — authenticates with the App
    /// Service's managed identity. In development the same credential falls back to the signed-in
    /// Azure CLI or Visual Studio account, so a developer runs the portal with no secret on disk.
    /// </summary>
    public static IServiceCollection AddPmoInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PortalOptions>(configuration.GetSection(PortalOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<RoleGroupOptions>(configuration.GetSection(RoleGroupOptions.SectionName));

        var connectionString = configuration.GetConnectionString("PmoDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PmoDatabase is not configured. In Azure this is the SQL connection "
                + "string with Authentication=Active Directory Default and no password.");

        services.AddSingleton<ISqlConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

        var timeZone = configuration[$"{PortalOptions.SectionName}:TimeZone"] ?? "Asia/Dubai";
        services.AddSingleton<IClock>(_ => new SystemClock(timeZone));

        services.AddMemoryCache();

        var credential = new DefaultAzureCredential();
        services.AddSingleton<TokenCredential>(credential);

        // Blob Storage — supporting documents (FR-26).
        var storageUri = configuration[$"{StorageOptions.SectionName}:ServiceUri"];
        if (!string.IsNullOrWhiteSpace(storageUri))
        {
            services.AddSingleton(_ => new BlobServiceClient(new Uri(storageUri), credential));
            services.AddScoped<IDocumentStore, BlobDocumentStore>();
        }
        else
        {
            // No storage account configured: the portal runs, and attaching a document says so
            // rather than failing with a null reference somewhere deeper.
            services.AddScoped<IDocumentStore, UnconfiguredDocumentStore>();
        }

        // Microsoft Graph — notifications (section 5.3) and the directory picker (FR-04).
        services.AddSingleton(_ => new GraphServiceClient(
            credential, ["https://graph.microsoft.com/.default"]));
        services.AddScoped<IEmailSender, GraphEmailSender>();
        services.AddScoped<IDirectoryService, GraphDirectoryService>();

        services.AddScoped<IProjectAccessService, ProjectAccessService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IUpdateService, UpdateService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IRiskService, RiskService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IExportService, ExportService>();

        // WF-04, WF-05, WF-07.
        services.AddHostedService<NotificationScheduler>();

        // Refuses to start if the database's controlled lists have drifted from the enums.
        services.AddHostedService<DatabaseHealthCheck>();

        return services;
    }
}

/// <summary>Stands in when no storage account is configured, so the reason is legible.</summary>
internal sealed class UnconfiguredDocumentStore : IDocumentStore
{
    private const string Message =
        "Document storage is not configured. Set Storage:ServiceUri to the blob service URI of the "
        + "PMO360 storage account.";

    public Task<StoredDocument> SaveAsync(
        Stream content, string fileName, string contentType, string projectCode, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<DocumentContent?> OpenAsync(string blobName, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<Uri?> TryGetReadLinkAsync(
        string blobName, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        Task.FromResult<Uri?>(null);
}
