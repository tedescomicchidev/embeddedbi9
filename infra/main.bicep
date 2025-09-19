param location string = resourceGroup().location
// Removed unused environment label to avoid clash with environment() intrinsic
// param environment string = 'dev'
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
// Application Insights removed (provider not registered). If re-added, restore variable and resource.
// Key Vault name: allow override; default makes it globally unique & <=24 chars (alphanumeric only)
@minLength(3)
@maxLength(24)
param keyVaultName string = toLower(replace(substring('${namePrefix}kv${uniqueString(subscription().id, resourceGroup().id)}', 0, 24), '-', ''))
var addressSpace = '10.10.0.0/16'
var appSubnetPrefix = '10.10.1.0/24'
var privateEndpointsSubnetPrefix = '10.10.2.0/24'
// Private DNS zone for blob constructed from current cloud suffix
var blobPrivateLinkZone = 'privatelink.blob.${az.environment().suffixes.storage}'

// VNet with subnets
resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: '${namePrefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressSpace
      ]
    }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: appSubnetPrefix
          delegations: [
            {
              name: 'webdelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointsSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// App subnet id (used for Web App regional VNet integration)
var appSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'apps')
var peSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'private-endpoints')

// App Service Plan
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: 'S1'
    tier: 'Standard'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// Storage (private access only)
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Disabled'
    supportsHttpsTrafficOnly: true
  }
}

// (Application Insights intentionally removed)

// Key Vault
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: []
    enabledForTemplateDeployment: true
  }
}

resource pbiClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'PBI-Client-Secret'
  parent: kv
  properties: {
    value: pbiClientSecret
  }
}

// (Removed use of storage account key for Functions runtime; shifting to managed identity binding)
// Key Vault secret URI (no version) used for app setting reference (avoid hardcoded host)
var pbiClientSecretUri = '${kv.properties.vaultUri}secrets/PBI-Client-Secret'

// Web App
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Integrate Web App with delegated subnet so it can resolve private endpoints
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
      vnetRouteAllEnabled: true
      appSettings: [
        {
          name: 'PBI_CLIENT_ID'
          value: pbiClientId
        }
        {
          name: 'PBI_TENANT_ID'
          value: pbiTenantId
        }
        {
          name: 'FUNCTION_API_BASE_URL'
          // Use private link FQDN so call stays on private network
          value: 'https://${functionAppName}.privatelink.azurewebsites.net'
        }
      ]
    }
  }
  identity: { type: 'SystemAssigned' }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    // Disable public ingress; only private endpoint (and VNet-integrated callers) can reach it
    publicNetworkAccess: 'Disabled'
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET|8.0'
      // Optional hard lock: deny all IPs (portal may show as restrictive). You can later add specific rules if needed.
      ipSecurityRestrictions: [
        {
          name: 'DenyAll'
          // Removed ';' to satisfy API validation (no semicolons or commas allowed in description)
          description: 'Block all public traffic and rely on Private Endpoint'
          action: 'Deny'
          priority: 100
          ipAddress: '0.0.0.0/0'
        }
      ]
      appSettings: [
        // Identity-based storage (no key). The following settings instruct the Functions runtime
        // to authenticate to the Storage Account using Managed Identity per
        // https://learn.microsoft.com/azure/azure-functions/storage-considerations?tabs=azure-portal#configure-identity-based-connections
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: 'https://${storage.name}.blob.${az.environment().suffixes.storage}'
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: 'https://${storage.name}.queue.${az.environment().suffixes.storage}'
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: 'https://${storage.name}.table.${az.environment().suffixes.storage}'
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri__credential'
          value: 'managedidentity'
        }
        {
          name: 'PBI_CLIENT_ID'
          value: pbiClientId
        }
        {
          name: 'PBI_TENANT_ID'
          value: pbiTenantId
        }
        {
          name: 'PBI_CLIENT_SECRET'
          value: '@Microsoft.KeyVault(SecretUri=${pbiClientSecretUri})'
        }
        {
          name: 'USER_CSV_CONTAINER'
          value: 'data'
        }
        {
          name: 'USER_CSV_FILENAME'
          value: 'user_locations.csv'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '0'
        }
      ]
    }
  }
  dependsOn: [ pbiClientSecretSecret ]
}

// Blob container
resource dataContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storage.name}/default/data'
  properties: {}
  // dependsOn removed (implicit via parent resource reference)
}

// Key Vault access policy
resource kvPolicies 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  name: 'add'
  parent: kv
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
  // dependsOn removed (implicit via principalId reference)
}

// Private DNS zones
resource dnsZoneWeb 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.azurewebsites.net'
  location: 'global'
}

resource dnsZoneBlob 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: blobPrivateLinkZone
  location: 'global'
}

resource dnsLinkWeb 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: '${namePrefix}-link-web'
  parent: dnsZoneWeb
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource dnsLinkBlob 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: '${namePrefix}-link-blob'
  parent: dnsZoneBlob
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

// Private Endpoint: Function
resource peFunction 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${namePrefix}-pe-func'
  location: location
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'funcConnection'
        properties: {
          privateLinkServiceId: functionApp.id
          groupIds: [
            'sites'
          ]
          requestMessage: 'Private access to Function App'
        }
      }
    ]
  }
  // dependsOn removed (implicit via references)
}

resource peFunctionDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  name: 'default'
  parent: peFunction
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'webconfig'
        properties: {
          privateDnsZoneId: dnsZoneWeb.id
        }
      }
    ]
  }
}

// Private Endpoint: Storage (Blob)
resource peStorageBlob 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${namePrefix}-pe-storblob'
  location: location
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'storBlobConnection'
        properties: {
          privateLinkServiceId: storage.id
          groupIds: [
            'blob'
          ]
          requestMessage: 'Private access to Storage Blob'
        }
      }
    ]
  }
  // dependsOn removed (implicit via references)
}

// RBAC: Grant Function App managed identity access to Storage (Blob & Queue Data Contributor)
resource storageBlobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storage.id, functionApp.name, 'blob-data')
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe') // Storage Blob Data Contributor
    principalType: 'ServicePrincipal'
  }
  // implicit dependency via principalId reference
}

resource storageQueueDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storage.id, functionApp.name, 'queue-data')
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88') // Storage Queue Data Contributor
    principalType: 'ServicePrincipal'
  }
  // implicit dependency via principalId reference
}

resource peStorageBlobDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  name: 'default'
  parent: peStorageBlob
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blobconfig'
        properties: {
          privateDnsZoneId: dnsZoneBlob.id
        }
      }
    ]
  }
}

// Outputs
output webAppHostname string = webApp.properties.defaultHostName
output functionHostname string = functionApp.properties.defaultHostName
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output functionUrl string = 'https://${functionApp.properties.defaultHostName}'
output vnetId string = vnet.id
output functionPrivateEndpointId string = peFunction.id
output storageBlobPrivateEndpointId string = peStorageBlob.id
output keyVaultDeployedName string = kv.name
