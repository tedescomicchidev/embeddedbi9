mkdir infra
mkdir src
cd src
dotnet new mvc -n identity-client-web-app --use-program-main
cd identity-client-web-app/
dotnet add package Microsoft.Identity.Web.UI
Install Func Core: https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=linux%2Cisolated-process%2Cnode-v4%2Cpython-v2%2Chttp-trigger%2Ccontainer-apps&pivots=programming-language-csharp
func init identity-client-api --worker-runtime dotnet --target-framework net8.0
cd identity-client-api
func new --name GetEmbedToken --template "HTTP trigger" --authlevel function
dotnet add package Azure.Identity
dotnet add package Microsoft.PowerBI.Api
dotnet dev-certs https --trust
dotnet dev-certs https --check --verbose
dotnet dev-certs https -ep ./aspnetcore-dev-cert.pfx -p password