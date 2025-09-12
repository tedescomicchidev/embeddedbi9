I want to build a dotnet web app (identity-client-web-app), which calls a dotnet azure function (identity-client-api) and retrieves a powerbi embed token.

Read carefully this template definition: https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-templates?tabs=csharp

I want to include the infra deployments of the identity-client-web-app and the identity-client-api in bicep.

The identity-client-web-app should display a PowerBi report.

The identity-client-web-app should call the azure function to request an powerbi embed token.

The Function should follow this sample https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-sample-for-your-organization?tabs=net-core

The Function should call the PowerBI embed token api and return the token to the web app: https://learn.microsoft.com/en-us/rest/api/power-bi/embed-token/generate-token 

The Azure function should be called via a Service-class and not directly from the controller of the web app.

The workspace ID and report ID should be passed as parameters to the function and not read from the function configuration.

The web app should send also the username from Entra, the user groups from entra and the user location from the browser. The Azure function needs to receive those parameters and evaluate whether they've been filled.

The project needs to read a csv file in the function app. The csv file needs to contain a list of users and their respective country. 

The function needs to read the csv from a local azure storage account emulator.

Ensure that the function checks whether the user passed from the web app parameter and the user Location from the parameter is listed in the csv file. If there is a match, an embed token can be crated. if there is not match, no embed token is returned.