// The container registry of one environment (task 024). Deployed before main.bicep, which refers to it as existing.
targetScope = 'resourceGroup'

@allowed(['dev', 'staging', 'prod'])
param environmentName string

param location string = resourceGroup().location

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${replace('cesio-${environmentName}', '-', '')}acr'
  location: location
  tags: { application: 'cesio', environment: environmentName }
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

output loginServer string = registry.properties.loginServer
output name string = registry.name
