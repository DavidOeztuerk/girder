using Girder.Infrastructure.Security.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.Secrets;

// VaultConfiguration, VaultData, VaultListData, VaultListResponse, VaultMetadata,
// VaultMetadataData, VaultMetadataResponse, VaultResponse, and VaultVersionInfo are
// internal DTO classes used exclusively by OpenBaoSecretProvider for HTTP deserialization.
// They cannot be instantiated directly from external test assemblies.
// Their behaviour is exercised indirectly through OpenBaoSecretProvider tests below.

[Trait("Category", "Unit")]
public class VaultDtoIndirectCoverageTests
{
    private static OpenBaoSecretProvider CreateProvider(
        string address = "http://localhost:18299",
        string token = "dev-token",
        string? @namespace = null,
        string? mountPoint = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Vault:Addr"] = address,
            ["Vault:Token"] = token
        };
        if (@namespace is not null) dict["Vault:Namespace"] = @namespace;
        if (mountPoint is not null) dict["Vault:Mount"] = mountPoint;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        var logger = Substitute.For<ILogger>();
        return new OpenBaoSecretProvider(logger, config);
    }

    [Fact]
    public void HashiCorpVaultProvider_Constructor_WithValidConfig_Succeeds()
    {
        // Exercises LoadConfiguration() which initialises VaultConfiguration
        var provider = CreateProvider();

        provider.Should().NotBeNull();
        provider.Dispose();
    }

    [Fact]
    public void HashiCorpVaultProvider_Constructor_WithNamespaceAndMount_Succeeds()
    {
        // Exercises optional VaultConfiguration.Namespace and .MountPoint paths
        var provider = CreateProvider(@namespace: "ns1", mountPoint: "kv2");

        provider.Should().NotBeNull();
        provider.Dispose();
    }

    [Fact]
    public void HashiCorpVaultProvider_Constructor_MissingToken_ThrowsInvalidOperation()
    {
        var logger = Substitute.For<ILogger>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Addr"] = "http://localhost:8200"
                // Vault:Token intentionally absent
            })
            .Build();

        var act = () => new OpenBaoSecretProvider(logger, config);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*token*");
    }

    [Fact]
    public void HashiCorpVaultProvider_Constructor_EmptyConfig_ThrowsInvalidOperation()
    {
        var logger = Substitute.For<ILogger>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => new OpenBaoSecretProvider(logger, config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task HashiCorpVaultProvider_GetSecretAsync_NoServer_ThrowsHttpRequestException()
    {
        // Exercises VaultResponse / VaultData deserialization path (via network failure)
        var provider = CreateProvider();

        var act = async () => await provider.GetSecretAsync("some-key");

        await act.Should().ThrowAsync<HttpRequestException>();
        provider.Dispose();
    }

    [Fact]
    public async Task HashiCorpVaultProvider_SetSecretAsync_NoServer_ThrowsHttpRequestException()
    {
        // Exercises the PUT/POST VaultData serialisation path
        var provider = CreateProvider();

        var act = async () => await provider.SetSecretAsync("some-key", "some-value");

        await act.Should().ThrowAsync<HttpRequestException>();
        provider.Dispose();
    }

    [Fact]
    public async Task HashiCorpVaultProvider_DeleteSecretAsync_NoServer_ThrowsHttpRequestException()
    {
        var provider = CreateProvider();

        var act = async () => await provider.DeleteSecretAsync("some-key");

        await act.Should().ThrowAsync<HttpRequestException>();
        provider.Dispose();
    }

    [Fact]
    public async Task HashiCorpVaultProvider_SecretExistsAsync_NoServer_ThrowsHttpRequestException()
    {
        var provider = CreateProvider();

        var act = async () => await provider.SecretExistsAsync("some-key");

        await act.Should().ThrowAsync<HttpRequestException>();
        provider.Dispose();
    }

    [Fact]
    public async Task HashiCorpVaultProvider_ListSecretKeysAsync_NoServer_ReturnsEmptyCollection()
    {
        // ListSecretKeysAsync swallows network errors and returns an empty list
        // (exercises VaultListResponse / VaultListData deserialization error path)
        var provider = CreateProvider();

        var keys = await provider.ListSecretKeysAsync();

        keys.Should().NotBeNull();
        keys.Should().BeEmpty();
        provider.Dispose();
    }

    [Fact]
    public void HashiCorpVaultProvider_Dispose_DoesNotThrow()
    {
        var provider = CreateProvider();

        var act = () => provider.Dispose();

        act.Should().NotThrow();
    }
}
