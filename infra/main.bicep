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
var addressSpace = '10.10.0.0/16'
var appSubnetPrefix = '10.10.1.0/24'
var privateEndpointsSubnetPrefix = '10.10.2.0/24'

// VNet with subnets
resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: '${namePrefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressSpace
      ]
  },
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: appSubnetPrefix
          delegation: [
            {
              name: 'webdelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      },
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

var appSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'apps')
var peSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'private-endpoints')

// App Service Plan
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
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

// App Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// Key Vault
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
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
  name: '${kv.name}/PBI-Client-Secret'
  properties: {
    value: pbiClientSecret
  }
  dependsOn: [
    kv
  ]
}

var pbiClientSecretUri = pbiClientSecretSecret.properties.secretUriWithVersion

// Storage connection string (removed environment().suffixes.storage)
var storageKey = storage.listKeys().keys[0].value
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storageKey};EndpointSuffix=core.windows.net'

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
    siteConfig: {
      virtualNetworkSubnetId: appSubnetId
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        },
        {
          name: 'PBI_CLIENT_ID'
          value: pbiClientId
        },
        {
          name: 'PBI_TENANT_ID'
          value: pbiTenantId
        },
        {
          name: 'FUNCTION_API_BASE_URL'
          value: 'https://${functionAppName}.azurewebsites.net'
        }
      ]
    }
  }
  dependsOn: [
    plan
    appInsights
  ]
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
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET|8.0'
      virtualNetworkSubnetId: appSubnetId
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        },
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        },
        {
          name: 'PBI_CLIENT_ID'
          value: pbiClientId
        },
        {
          name: 'PBI_TENANT_ID'
          value: pbiTenantId
        },
        {
          name: 'PBI_CLIENT_SECRET'
          value: '@Microsoft.KeyVault(SecretUri=${pbiClientSecretUri})'
        },
        {
          name: 'USER_CSV_CONTAINER'
          value: 'data'
        },
        {
          name: 'USER_CSV_FILENAME'
          value: 'user_locations.csv'
        },
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        },
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '0'
        }
      ]
    }
  }
  dependsOn: [
    storage
    plan
    appInsights
    pbiClientSecretSecret
  ]
}

// Blob container
resource dataContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storage.name}/default/data'
  properties: {}
  dependsOn: [
    storage
  ]
}

// Key Vault access policy
resource kvPolicies 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  name: '${kv.name}/add'
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
  dependsOn: [
    functionApp
    kv
  ]
}

// Private DNS zones
resource dnsZoneWeb 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.azurewebsites.net'
  location: 'global'
}

resource dnsZoneBlob 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.blob.core.windows.net'
  location: 'global'
}

resource dnsLinkWeb 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: '${dnsZoneWeb.name}/${namePrefix}-link-web'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
  dependsOn: [
    dnsZoneWeb
    vnet
  ]
}

resource dnsLinkBlob 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: '${dnsZoneBlob.name}/${namePrefix}-link-blob'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
  dependsOn: [
    dnsZoneBlob
    vnet
  ]
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
  dependsOn: [
    functionApp
    vnet
  ]
}

resource peFunctionDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  name: '${peFunction.name}/default'
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
  dependsOn: [
    peFunction
    dnsZoneWeb
    dnsLinkWeb
  ]
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
  dependsOn: [
    storage
    vnet
  ]
}

resource peStorageBlobDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  name: '${peStorageBlob.name}/default'
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
  dependsOn: [
    peStorageBlob
    dnsZoneBlob
    dnsLinkBlob
  ]
}

// Outputs
output webAppHostname string = webApp.properties.defaultHostName
output functionHostname string = functionApp.properties.defaultHostName
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output functionUrl string = 'https://${functionApp.properties.defaultHostName}'
output vnetId string = vnet.id
output functionPrivateEndpointId string = peFunction.id
output storageBlobPrivateEndpointId string = peStorageBlob.id
