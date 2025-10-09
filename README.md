## Power BI Embedded Sample (Web App + Azure Function)

This repository contains:

- ASP.NET Core MVC web app (`identity-client-web-app`) authenticating users via Microsoft Entra ID and embedding a Power BI report using an embed token.
- Azure Functions app (`identity-client-api`) exposing an endpoint to generate Power BI embed tokens after validating user + location against a CSV stored in Blob Storage.
- Bicep infrastructure (`infra/`) and `azure.yaml` to support Azure Developer CLI (`azd`) deployments.

### High-Level Flow
1. User signs into web app (OpenID Connect via Microsoft.Identity.Web).
2. User enters Workspace Id and Report Id.
3. Web app server-side calls the internal `whereAmI` service (new Azure Function) to resolve the user's location & channel.
4. Web app posts to its own `/EmbedToken` endpoint (without client geolocation); server supplies the location to the embed token API.
5. Embed token Function validates `Username` + location against `user_locations.csv`.
6. If authorized, function calls Power BI REST API and returns an embed token.
7. Web app embeds the report using Power BI JavaScript SDK.

### Projects
| Project | Purpose |
|---------|---------|
| `src/identity-client-web-app` | MVC app + auth + embedding UI |
| `src/identity-client-api` | Azure Function generating embed tokens |
| `src/whereami-function` | Azure Function (simulation) returning constant location/channel |
| `infra/` | Bicep templates (App Service, Function, Storage, Key Vault, App Insights) |

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools
- Azure CLI + Azure Developer CLI (`azd`)
- Azurite (local storage emulator) or real Azure Storage
- A Power BI workspace, report, and a service principal with access (plus tenant admin enabled service principal usage for Power BI)

### Configuration (Environment Variables)
Function expects:
```
PBI_TENANT_ID
PBI_CLIENT_ID
PBI_CLIENT_SECRET
USER_CSV_CONTAINER=data
USER_CSV_FILENAME=user_locations.csv
```

Web app uses:
```
FunctionApi:BaseUrl (appsettings or environment)
AzureAd (standard Microsoft.Identity.Web settings)
```

### Local Development
1. Start Azurite (if using emulator):
```bash
npx azurite --location ./azurite --debug ./azurite/debug.log
```
2. Ensure `local.settings.json` in function has `UseDevelopmentStorage=true`.
3. Upload CSV (Azurite) using Azure Storage Explorer or Azurite REST:
	- Create container `data`
	- Upload `data/user_locations.csv`
4. Set environment variables (Power BI service principal creds). For bash:
```bash
export PBI_TENANT_ID=YOUR_TENANT_ID
export PBI_CLIENT_ID=YOUR_CLIENT_ID
export PBI_CLIENT_SECRET=YOUR_CLIENT_SECRET
```
5. Run functions:
```bash
# Terminal 1: whereAmI service (returns CH/05)
cd src/whereami-function
func start

# Terminal 2: embed token function
cd ../identity-client-api
func start
```
6. Run web app:
```bash
cd ../identity-client-web-app
dotnet run
```
7. Navigate to https://localhost:5001 (or shown port), sign in, enter Workspace & Report Ids, embed.

#### whereAmI Service
The `whereAmI` Azure Function provides a simple HTTP GET endpoint:

`GET http://localhost:7071/api/whereami`

Response:
```json
{ "location": "CH", "channel": "05" }
```

The web app consumes this service during page load (server-side) so no browser geolocation APIs are used, avoiding permission prompts and client tampering of location (still spoofable, but centralized).

### Debugging (VS Code)
Launch configurations provided:

- `Functions: Start Host Task` – starts the Azure Functions host via the background task `func host start (identity-client-api)`. This launches the host; breakpoints may not bind until symbols load.
- `Functions: Attach` – attach manually to a running Functions host (use this after starting host if breakpoints did not bind automatically).
- `Web: identity-client-web-app` – launches the MVC app.
- Compound: `Start Web + Functions Host` – starts the Functions host task and the web app in sequence.

Recommended workflow for debugging function code:
1. Put breakpoints in `GetEmbedToken.cs`.
2. Start the compound `Start Web + Functions Host`.
3. If a breakpoint does not hit, run `Functions: Attach` and pick the `dotnet` process hosting the function.
4. Invoke the endpoint (submit form in the web UI) to trigger breakpoint.

If you prefer a single-step attach experience, you can remove the host task config and rely solely on `func start` launched manually in a terminal, then use `Functions: Attach`.

#### Local Function over HTTP
The Azure Functions Core Tools host serves locally over HTTP (http://localhost:7071). Attempting to call it via HTTPS without configuring a reverse proxy or certificate will produce SSL handshake errors like:
```
Cannot determine the frame size or a corrupted frame was received.
```
The appsettings now use `http://localhost:7071` for `FunctionApi:BaseUrl`. The `EmbedService` contains a safeguard that, if you intentionally point to `https://localhost:7071` and an SSL failure occurs, it retries automatically over HTTP for developer convenience.

### Deployment with azd
1. Login: `azd auth login`
2. Initialize: `azd init` (accept existing).
3. Provision + Deploy: `azd up -e dev` (provide required parameters). You can parameterize through `azd env set` or interactive prompts.

### CSV Authorization Logic
`user_locations.csv` lines formatted as:
```
userPrincipalName,countryOrLocation
```
Example:
```
alice@example.com,US
```
If the authenticated user's username and the location string provided from the browser match a line, an embed token is issued. Otherwise 403.

### Power BI Considerations
- Service principal must be granted workspace access.
- Tenant setting "Allow service principals to use Power BI APIs" must be enabled.
- Embed token supports the single report/dataset provided.

### Security Notes
- Client secret should be stored in Key Vault in production; current bicep sets a plain app setting for simplicity (improve by adding managed identity + Key Vault reference).
- Geolocation is approximate and user controlled; consider server-side enrichment or IP-based location for stronger assurance.

### Next Improvements
- Add unit tests for function logic (CSV parsing, authorization).
- Add caching for CSV lookups.
- Replace plain secret with Key Vault reference + managed identity.
- Support user groups filtering for RLS datasets.

### Original Scaffolding Commands (for reference)
```
dotnet new mvc -n identity-client-web-app --use-program-main
func init identity-client-api --worker-runtime dotnet --target-framework net8.0
func new --name GetEmbedToken --template "HTTP trigger" --authlevel function
```