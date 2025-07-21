# SharedTools Configuration Management - TODO

## Problem Statement
Multiple modules need blob storage configuration (connection strings, container names) but we want to avoid:
- Host needing to know about every module's configuration structure
- Tight coupling between host and modules
- Manual configuration for each module in appsettings.json

## Potential Solutions to Explore

### 1. Convention-Based Configuration
**Idea**: Modules look for configuration in predictable locations
- [ ] Define convention: `Modules:{ModuleName}:BlobStorage`
- [ ] Create base configuration classes modules can inherit
- [ ] Document standard configuration keys

**Pros**: Simple, predictable
**Cons**: Still requires host knowledge of each module

### 2. Shared Configuration Provider
**Idea**: A shared component that provides common services configuration
- [ ] Create SharedTools.Configuration package
- [ ] Define IBlobStorageConfiguration interface
- [ ] Implement configuration provider that modules can request
- [ ] Support multiple storage accounts with named configurations

**Pros**: Decouples configuration, enables sharing
**Cons**: Another dependency, needs design work

### 3. Environment Variable Conventions
**Idea**: Use environment variables with module prefixes
- [ ] Define pattern: `SHAREDTOOLS_{MODULE}_{SETTING}`
- [ ] Create helper to read module-specific env vars
- [ ] Fall back to appsettings.json if not found

**Pros**: Works everywhere, no host changes needed
**Cons**: Less discoverable, harder to manage many settings

### 4. Configuration Discovery/Registration
**Idea**: Modules declare their configuration needs
- [ ] Add ConfigurationRequirements to IApplicationPartModule
- [ ] Generate configuration template/documentation
- [ ] Validate configuration at startup
- [ ] Provide UI in ModuleManagement for configuration

**Pros**: Self-documenting, validation
**Cons**: More complex, requires interface changes

### 5. Shared Service Abstraction
**Idea**: Abstract blob storage behind shared interface
- [ ] Create SharedTools.Storage package
- [ ] Define IModuleStorage interface
- [ ] Host provides implementation
- [ ] Modules request storage by name/purpose

**Pros**: Maximum decoupling, enables different backends
**Cons**: Requires all modules to change

### 6. Configuration Inheritance/Defaults
**Idea**: Cascade configuration from defaults → shared → module-specific
- [ ] Define default blob storage settings
- [ ] Allow module-specific overrides
- [ ] Support connection string reuse with different containers

**Pros**: Reduces redundancy, flexible
**Cons**: Can be confusing to debug

## Recommended Approach (Hybrid)

Combine several approaches for flexibility:

1. **Immediate**: Convention-based with environment variable fallback
2. **Short-term**: Shared configuration provider for common services
3. **Long-term**: Module configuration discovery/validation

## Implementation Tasks

### Phase 1: Quick Win (Convention + Environment)
- [ ] Document configuration conventions in MIGRATION.md
- [ ] Create ConfigurationHelper extension methods
- [ ] Update example modules to use conventions
- [ ] Support both appsettings and environment variables

### Phase 2: Shared Configuration (1-2 weeks)
- [ ] Design SharedTools.Configuration package
- [ ] Define interfaces for common configurations:
  - [ ] IBlobStorageConfiguration
  - [ ] IDatabaseConfiguration
  - [ ] IApiKeyConfiguration
- [ ] Implement configuration providers
- [ ] Create configuration builder extensions

### Phase 3: Module Configuration Discovery (Future)
- [ ] Extend IApplicationPartModule with optional interface
- [ ] Build configuration requirements system
- [ ] Add validation at startup
- [ ] Create ModuleManagement UI for configuration

## Code Examples to Create

### Configuration Helper (Phase 1)
```csharp
public static class ModuleConfigurationExtensions
{
    public static IConfigurationSection GetModuleConfiguration(
        this IConfiguration configuration, 
        string moduleName)
    {
        // Try environment variables first
        var envPrefix = $"SHAREDTOOLS_{moduleName.ToUpper()}_";
        // Fall back to appsettings.json section
        return configuration.GetSection($"Modules:{moduleName}");
    }
}
```

### Shared Configuration Interface (Phase 2)
```csharp
public interface IBlobStorageConfiguration
{
    string GetConnectionString(string name = "default");
    string GetContainerName(string purpose);
}
```

### Module Declaration (Phase 3)
```csharp
public interface IConfigurableModule
{
    ConfigurationRequirements GetRequirements();
}

public class ConfigurationRequirements
{
    public List<ConfigurationItem> Required { get; set; }
    public List<ConfigurationItem> Optional { get; set; }
}
```

## Cloud-Based Secret Management Solutions

### 7. Azure Key Vault Integration
**Idea**: Centralized secret management using Azure Key Vault
- [ ] Create SharedTools.Configuration.AzureKeyVault package
- [ ] Implement IConfigurationBuilder extension for Key Vault
- [ ] Use managed identity for authentication
- [ ] Cache secrets with TTL for performance

**Pattern**:
```
ModuleName/BlobStorage/ConnectionString
ModuleName/BlobStorage/ContainerName
ModuleName/ApiKeys/ServiceXKey
```

**Pros**: 
- Centralized secret management
- Audit trail and access control
- Rotation support
- No secrets in config files

**Cons**: 
- Azure dependency
- Additional cost
- Network latency

### 8. AWS Secrets Manager Integration
**Idea**: AWS equivalent for secret management
- [ ] Create SharedTools.Configuration.AwsSecrets package
- [ ] Implement IConfigurationBuilder extension for Secrets Manager
- [ ] Use IAM roles for authentication
- [ ] Support automatic rotation

**Pattern**:
```
/sharedtools/modulename/blobstorage
/sharedtools/modulename/apikeys
```

**Pros**: 
- Similar benefits to Key Vault
- Native AWS integration
- Automatic rotation capabilities
- Version history

**Cons**: 
- AWS dependency
- Cost per secret
- Regional considerations

### 9. Multi-Cloud Abstraction
**Idea**: Abstract secret management across providers
- [ ] Create ISecretProvider interface
- [ ] Implement for Azure Key Vault
- [ ] Implement for AWS Secrets Manager
- [ ] Implement for HashiCorp Vault (self-hosted option)
- [ ] Local file provider for development

**Interface**:
```csharp
public interface ISecretProvider
{
    Task<string> GetSecretAsync(string path);
    Task<T> GetSecretAsync<T>(string path);
    Task SetSecretAsync(string path, string value);
}
```

## Infrastructure Considerations

### Development vs Production
- [ ] Local secrets.json for development
- [ ] Environment-based provider selection
- [ ] Docker secrets support
- [ ] Kubernetes secrets integration

### Performance & Caching
- [ ] In-memory caching with TTL
- [ ] Redis distributed cache option
- [ ] Bulk secret loading at startup
- [ ] Change notification support

### Security & Compliance
- [ ] Audit logging for secret access
- [ ] Role-based access control
- [ ] Encryption at rest and in transit
- [ ] Secret rotation policies

### High Availability
- [ ] Multi-region secret replication
- [ ] Fallback providers
- [ ] Circuit breaker pattern
- [ ] Health checks for secret providers

## Recommended Architecture

### Three-Tier Configuration System

1. **Secret Provider Layer** (Cloud/Vault)
   - Connection strings
   - API keys
   - Certificates
   
2. **Configuration Layer** (AppSettings/Environment)
   - Non-sensitive settings
   - Feature flags
   - Module behavior settings
   
3. **Convention Layer** (Code)
   - Default values
   - Computed settings
   - Module-specific logic

### Implementation Priority

1. **Phase 1**: Local Development
   - Convention-based configuration
   - User secrets for local dev
   - Environment variables

2. **Phase 2**: Cloud Integration
   - Azure Key Vault for Azure users
   - AWS Secrets Manager for AWS users
   - Abstract interface for both

3. **Phase 3**: Advanced Features
   - Multi-cloud support
   - Secret rotation
   - Audit and compliance

## Module Configuration Example

```csharp
public class YourModule : IApplicationPartModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlobStorage>(sp =>
        {
            var secrets = sp.GetRequiredService<ISecretProvider>();
            var config = sp.GetRequiredService<IConfiguration>();
            
            // Try secret provider first
            var connectionString = await secrets.GetSecretAsync(
                $"{Name}/BlobStorage/ConnectionString"
            );
            
            // Fall back to configuration
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = config[$"Modules:{Name}:BlobStorage:ConnectionString"];
            }
            
            return new BlobStorage(connectionString);
        });
    }
}
```

## Questions to Resolve

1. Should we support multiple storage accounts per module?
2. How to handle secrets (connection strings) securely?
   - Answer: Use cloud secret providers with local fallback
3. Should configuration be mutable at runtime?
4. How to handle configuration in development vs production?
   - Answer: Provider abstraction with environment-based selection
5. Should modules be able to share storage accounts/containers?
6. Which cloud providers to support initially?
7. How to handle multi-cloud deployments?
8. Pricing implications of secret storage?

## Next Steps

1. Decide on initial cloud provider support (Azure, AWS, both?)
2. Design ISecretProvider interface
3. Implement Phase 1 configuration helpers
4. Create proof-of-concept with one module
5. Document configuration patterns
6. Plan cloud provider integration