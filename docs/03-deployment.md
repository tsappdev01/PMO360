# Deployment to Azure

What the portal needs, and how it authenticates. The theme throughout is that there is no
secret in configuration: the App Service's managed identity is the principal for SQL, Blob
Storage and Microsoft Graph.

## Resources

| Resource | Notes |
| --- | --- |
| App Service (Linux, .NET 8) | Blazor Server, so enable **ARR affinity**: a circuit belongs to one instance. Always On for the scheduler. |
| Azure SQL Database | Standard S2 or better for [500] projects. Entra ID admin set, so the app's identity can be added as a user. |
| Storage account | One private container, `project-documents`. No public access, no anonymous blobs. |
| App registration (Entra ID) | Single sign-on and Graph. See below. |
| Application Insights | Recommended: the scheduler and the notification failures log there. |

## App registration

1. **Redirect URI** (Web): `https://<host>/signin-oidc`. Front-channel logout:
   `https://<host>/signout-callback-oidc`.
2. **Token configuration → groups claim**: security groups, emitted in the ID token. The portal
   reads the group ids and maps them to roles.
   *If the tenant's users are in many groups, Entra sends a `_claim_names` overage reference
   instead of the ids and no role will resolve. Use app roles instead in that case — assign the
   six roles to the groups and leave `Authorization:TrustAppRoleClaims` at `true`.*
3. **API permissions**, application (not delegated), with admin consent:
   - `Mail.Send` — the notifications of section 5.3.
   - `User.Read.All` — the directory picker (FR-04).
4. **Scope `Mail.Send` to the one mailbox.** Without this the registration can send as anyone in
   the tenant, which is far more than the BRD asks for:

   ```powershell
   New-ApplicationAccessPolicy -AppId <client-id> `
     -PolicyScopeGroupId pmo-senders@dubaiinvestments.com `
     -AccessRight RestrictAccess `
     -Description "PMO360 may send only as the PMO mailbox"
   ```

## Managed identity

Turn on the system-assigned identity, then grant it what it needs and nothing more:

```bash
# SQL — a contained user with EXECUTE on the procedure surface (db/030_permissions.sql)
#   run as the Entra admin on the database:
#   CREATE USER [app-pmo360-prod] FROM EXTERNAL PROVIDER;
#   GRANT EXECUTE ON SCHEMA::pmo TO [app-pmo360-prod];

# Blob Storage — data-plane access to the one container
az role assignment create \
  --assignee-object-id "$APP_PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.Storage/storageAccounts/$SA/blobServices/default/containers/project-documents"
```

`Storage Blob Data Contributor` on the container — not on the account — is what lets the portal
write documents and issue short-lived user delegation links for downloads.

## Application settings

Set these on the App Service. The connection string carries no password:

```
ConnectionStrings__PmoDatabase = Server=tcp:<sql>.database.windows.net,1433;Initial Catalog=PMO360;Authentication=Active Directory Default;Encrypt=True;
AzureAd__TenantId              = <tenant guid>
AzureAd__ClientId              = <app registration client id>
AzureAd__Domain                = dubaiinvestments.com
Storage__ServiceUri            = https://<storage>.blob.core.windows.net
Notifications__SenderAddress   = pmo@dubaiinvestments.com
Notifications__PmoAddress      = pmo@dubaiinvestments.com
Notifications__ManagementDistributionList = pmo-management@dubaiinvestments.com
Notifications__PortalBaseUrl   = https://pmo360.dubaiinvestments.com
Authorization__RoleGroups__PmoAdministrator__0 = <group object id>
Authorization__RoleGroups__Management__0       = <group object id>
Authorization__RoleGroups__ProjectManager__0   = <group object id>
Authorization__RoleGroups__ProjectOwner__0     = <group object id>
Authorization__RoleGroups__Consultant__0       = <group object id>
Authorization__RoleGroups__ItSupport__0        = <group object id>
```

For UAT, add `Notifications__RedirectAllTo` with one address. Every notification then goes there
instead of to the business, so WF-01 to WF-08 can be exercised end to end without mailing the
board. `Notifications__Enabled=false` switches them off entirely.

## Order of deployment

1. Run the scripts in `db/` (see `db/README.md`), including `030_permissions.sql` with the right
   principal names.
2. Deploy the application.
3. Watch the first start. The portal verifies the database's controlled value lists against its
   own enums and **refuses to start** if they differ, naming the mismatch — that failure means a
   script has not been applied.

## After go-live

- Guest consultant accounts are reviewed at the end of each engagement (section 6). End the
  assignment rather than deleting it: access goes, the record of who held it stays.
- The scheduler runs on every instance and is safe to scale out — sends are claimed in
  `pmo.NotificationLog` before they go.
- Watch `pmo.NotificationLog` for rows where `Succeeded = 0`. A notification that could not be
  sent is a rule that did not reach anybody.
