// Cesio: one environment (dev, staging or prod) in its own resource group (task 024, architecture.md §2 and §6).
// Deployed only by .github/workflows/deploy.yml; never by hand against production (cesio-deploy skill).
targetScope = 'resourceGroup'

@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('EU region (ADR-016 Proposed, LQ-35 open): confirm before production.')
param location string = resourceGroup().location

@description('Digest of the signed image (sha256:…); images are deployed by digest, never by a mutable tag.')
param imageDigest string

@description('Object ID and display name of the Entra group that administers the SQL server (no SQL logins).')
param sqlAdminGroupObjectId string
param sqlAdminGroupName string

// Application settings that are not resource addresses (public URL, SEF environment, email, forwarded headers) are in
// src/Platform.Api|Worker/appsettings.{Staging,Production}.json, chosen by ASPNETCORE_ENVIRONMENT below.

var prod = environmentName == 'prod'
var prefix = 'cesio-${environmentName}'
var compact = replace(prefix, '-', '')
var tags = { application: 'cesio', environment: environmentName }

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: prod ? 365 : 30 }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id, DisableLocalAuth: true }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-app'
  location: location
  tags: tags
}

// The migration job's own identity: a member of the SQL admin group (one-time step, tasks/024 notes), so it may run DDL
// and manage roles; the app identity is only a member of app_rw.
resource migratorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-migrator'
  location: location
  tags: tags
}

// Created first by registry.bicep, so the image can be pushed before the apps exist.
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: '${compact}acr'
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

// Documents: private, soft delete and versioning (runbooks.md §3). Malware scanning: Defender for Storage on the subscription.
resource documents 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${compact}docs'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: prod ? 'Standard_ZRS' : 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource documentsBlob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: documents
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 30 }
    containerDeleteRetentionPolicy: { enabled: true, days: 30 }
  }
}

// Audit digests: WORM, a time-based immutability policy on the container (audit.md §1).
resource audit 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${compact}audit'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: prod ? 'Standard_ZRS' : 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource auditBlob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: audit
  name: 'default'
}

resource auditDigests 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: auditBlob
  name: 'audit-digests'
  properties: { publicAccess: 'None' }
}

resource auditWorm 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-05-01' = {
  parent: auditDigests
  name: 'default'
  properties: {
    // Retention period is open (LQ-38/LQ-47); 10 years as the working assumption of audit.md. Locked by hand after review.
    immutabilityPeriodSinceCreationInDays: 3650
    allowProtectedAppendWrites: false
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

resource sqlAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// General Purpose (ADR-021); serverless outside production. Tier and cost: tasks/024 notes.
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'cesio'
  location: location
  tags: tags
  sku: prod ? { name: 'GP_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 } : { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 1 }
  properties: {
    zoneRedundant: prod
    requestedBackupStorageRedundancy: prod ? 'Zone' : 'Local'
    autoPauseDelay: prod ? -1 : 60
  }
}

resource shortTermBackup 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01-preview' = {
  parent: database
  name: 'default'
  properties: { retentionDays: prod ? 35 : 7 }
}

resource longTermBackup 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01-preview' = if (prod) {
  parent: database
  name: 'default'
  properties: { weeklyRetention: 'P12W', monthlyRetention: 'P12M', yearlyRetention: 'P10Y', weekOfYear: 1 }
}

// Role assignments for the app identity: pull images, read and write secrets (SEF keys), read and write blobs.
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull) }
}

resource migratorAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, migratorIdentity.id, roles.acrPull)
  properties: { principalId: migratorIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull) }
}

resource vaultSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, identity.id, roles.keyVaultSecretsOfficer)
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer) }
}

resource documentsData 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: documents
  name: guid(documents.id, identity.id, roles.storageBlobDataContributor)
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor) }
}

// Contributor on the audit account still cannot change or delete a blob under the immutability policy.
resource auditData 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: audit
  name: guid(audit.id, identity.id, roles.storageBlobDataContributor)
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor) }
}

resource apps 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-apps'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: logs.listKeys().primarySharedKey }
    }
    zoneRedundant: false
  }
}

var image = '${registry.properties.loginServer}/cesio@${imageDigest}'
var aspnetEnvironment = prod ? 'Production' : 'Staging'
// Entra authentication to SQL: the API and worker as the app identity (app_rw), the migration job as its own identity.
var sqlServer = 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=cesio;Authentication=Active Directory Managed Identity;Encrypt=True;'
var sqlConnection = '${sqlServer}User Id=${identity.properties.clientId};'
var commonEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: aspnetEnvironment }
  { name: 'DOTNET_ENVIRONMENT', value: aspnetEnvironment }
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'ConnectionStrings__Cesio', value: sqlConnection }
  { name: 'ConnectionStrings__Documents', value: documents.properties.primaryEndpoints.blob }
  { name: 'ConnectionStrings__AuditDigests', value: audit.properties.primaryEndpoints.blob }
  { name: 'KeyVault__Uri', value: vault.properties.vaultUri }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
]
var registries = [{ server: registry.properties.loginServer, identity: identity.id }]
var userIdentity = { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  tags: tags
  identity: userIdentity
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: apps.id
    configuration: {
      // One active revision; rollback = activate the previous revision (tasks/024 notes).
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          args: ['api']
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: commonEnv
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
          ]
        }
      ]
      // One replica until data protection keys are shared through Blob + Key Vault (tasks/024 notes).
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-worker'
  location: location
  tags: tags
  identity: userIdentity
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: apps.id
    configuration: { activeRevisionsMode: 'Single', registries: registries }
    template: {
      containers: [
        {
          name: 'worker'
          image: image
          args: ['worker']
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: commonEnv
        }
      ]
      // Jobs are serialised by database locks, but one worker is enough and keeps the schedule simple.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource migrator 'Microsoft.App/jobs@2024-03-01' = {
  name: '${prefix}-migrator'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${migratorIdentity.id}': {} } }
  dependsOn: [migratorAcrPull]
  properties: {
    environmentId: apps.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registry.properties.loginServer, identity: migratorIdentity.id }]
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: image
          args: ['migrator']
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: aspnetEnvironment }
            { name: 'AZURE_CLIENT_ID', value: migratorIdentity.properties.clientId }
            { name: 'ConnectionStrings__Cesio', value: '${sqlServer}User Id=${migratorIdentity.properties.clientId};' }
            { name: 'Database__AppPrincipals__0__Name', value: identity.name }
            { name: 'Database__AppPrincipals__0__ClientId', value: identity.properties.clientId }
          ]
        }
      ]
    }
  }
}

// Production: the audit evidence, the database server and the vault cannot be deleted without removing the lock first.
resource auditLock 'Microsoft.Authorization/locks@2020-05-01' = if (prod) {
  scope: audit
  name: 'audit-no-delete'
  properties: { level: 'CanNotDelete', notes: 'Audit digests (WORM); audit.md §1.' }
}

resource sqlLock 'Microsoft.Authorization/locks@2020-05-01' = if (prod) {
  scope: sql
  name: 'sql-no-delete'
  properties: { level: 'CanNotDelete', notes: 'Production database server.' }
}

resource vaultLock 'Microsoft.Authorization/locks@2020-05-01' = if (prod) {
  scope: vault
  name: 'vault-no-delete'
  properties: { level: 'CanNotDelete', notes: 'SEF keys and other secrets.' }
}

output registryLoginServer string = registry.properties.loginServer
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output migratorJobName string = migrator.name
output apiAppName string = api.name
output workerAppName string = worker.name
