param location string = resourceGroup().location
param environment string = 'dev'
param namePrefix string

@description('Service principal client id for Power BI service principal')
param pbiClientId string
@secure()
param pbiClientSecret string
param pbiTenantId string

var webAppName = '${namePrefix}-web'
var functionAppName = '${namePrefix}-func'
var storageName = toLower(replace('${namePrefix}stor','-',''))
var planName = '${namePrefix}-plan'
var appInsightsName = '${namePrefix}-ai'
var kvName = '${namePrefix}-kv'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: 'B1' tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: { Application_Type: 'web' }
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A' name: 'standard' }
    accessPolicies: []
    enabledForTemplateDeployment: true
  }
}

resource pbiClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: '${kv.name}/PBI-Client-Secret'
  properties: { value: pbiClientSecret }
  dependsOn: [kv]
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING' value: appInsights.properties.ConnectionString }
        { name: 'PBI_CLIENT_ID' value: pbiClientId }
        { name: 'PBI_TENANT_ID' value: pbiTenantId }
        { name: 'FUNCTION_API_BASE_URL' value: 'https://${functionAppName}.azurewebsites.net' }
      ]
    }
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage' value: storage.properties.primaryEndpoints.blob }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING' value: appInsights.properties.ConnectionString }
        { name: 'PBI_CLIENT_ID' value: pbiClientId }
        { name: 'PBI_TENANT_ID' value: pbiTenantId }
        // secret reference typical format for system-assigned identity (not created here) placeholder
        { name: 'PBI_CLIENT_SECRET' value: pbiClientSecret }
        { name: 'USER_CSV_CONTAINER' value: 'data' }
        { name: 'USER_CSV_FILENAME' value: 'user_locations.csv' }
      ]
    }
  }
}

resource dataContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storage.name}/default/data'
  properties: {}
}

output webAppUrl string = webApp.properties.defaultHostName
output functionUrl string = functionApp.properties.defaultHostName