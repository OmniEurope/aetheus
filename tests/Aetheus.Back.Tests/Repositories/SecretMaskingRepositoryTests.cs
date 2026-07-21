// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class SecretMaskingRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SecretMaskingRepository _repo;

    public SecretMaskingRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new SecretMaskingRepository(_db);
    }

    [Fact]
    public async Task GetPipelineIdForRunAsync_Found_ReturnsPipelineId()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var pipeline = new Pipeline { Name = "Pipe", ProjectId = project.Id };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var run = new PipelineRun { PipelineId = pipeline.Id };
        _db.PipelineRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetPipelineIdForRunAsync(run.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(pipeline.Id, result);
    }

    [Fact]
    public async Task GetPipelineIdForRunAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetPipelineIdForRunAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPipelineYamlAndProjectAsync_Found_ReturnsTuple()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var pipeline = new Pipeline { Name = "Pipe", YamlDefinition = "stages:\n  - name: build", ProjectId = project.Id };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (yaml, projectId) = await _repo.GetPipelineYamlAndProjectAsync(pipeline.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal("stages:\n  - name: build", yaml);
        Assert.Equal(project.Id, projectId);
    }

    [Fact]
    public async Task GetPipelineYamlAndProjectAsync_NotFound_ReturnsNulls()
    {
        var (yaml, projectId) = await _repo.GetPipelineYamlAndProjectAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(yaml);
        Assert.Null(projectId);
    }

    [Fact]
    public async Task GetEncryptedSecretsAsync_ReturnsMatchingSecrets()
    {
        var vault = new Vault { Name = "my-vault", Description = "d" };
        _db.Vaults.Add(vault);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.VaultSecrets.AddRange(
            new VaultSecret { VaultId = vault.Id, Key = "secret1", EncryptedValue = "enc1" },
            new VaultSecret { VaultId = vault.Id, Key = "secret2", EncryptedValue = "enc2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEncryptedSecretsAsync(["my-vault"], null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Contains("enc1", result);
        Assert.Contains("enc2", result);
    }

    [Fact]
    public async Task GetEncryptedSecretsAsync_NoMatch_ReturnsEmpty()
    {
        var result = await _repo.GetEncryptedSecretsAsync(["nonexistent"], null, ct: TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEncryptedSecretsAsync_FiltersOnProjectId()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var vault1 = new Vault { Name = "vault-proj", Description = "d", ProjectId = project.Id };
        var vault2 = new Vault { Name = "vault-global", Description = "d", ProjectId = null };
        _db.Vaults.AddRange(vault1, vault2);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.VaultSecrets.AddRange(
            new VaultSecret { VaultId = vault1.Id, Key = "s1", EncryptedValue = "enc-proj" },
            new VaultSecret { VaultId = vault2.Id, Key = "s2", EncryptedValue = "enc-global" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEncryptedSecretsAsync(["vault-proj", "vault-global"], project.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    public void Dispose() => _db.Dispose();
}
