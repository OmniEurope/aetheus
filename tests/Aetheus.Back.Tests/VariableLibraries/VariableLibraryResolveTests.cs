// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class VariableLibraryResolveTests
{
    private readonly IVariableLibraryRepository _repo = Substitute.For<IVariableLibraryRepository>();
    private readonly VariableLibraryService _sut;

    public VariableLibraryResolveTests() =>
        _sut = new VariableLibraryService(_repo, Substitute.For<IDbTransactionScope>(),
            Substitute.For<IAuditService>(), Substitute.For<IEntityChangeNotifier>(),
            TimeProvider.System, Substitute.For<IMemoryCache>());

    private static VariableLibrary Lib(int? projectId, int? envId, int? projectServerId, string key, string value) =>
        new()
        {
            ProjectId = projectId,
            EnvironmentId = envId,
            ProjectServerId = projectServerId,
            Entries = [new VariableLibraryEntry { Key = key, Value = value }]
        };

    [Fact]
    public async Task ResolveWithCrossAccess_AppliesScopePrecedence()
    {
        // Same key at every scope: project-server scope must win (applied last).
        _repo.FindByNamesWithCrossAccessAsync(Arg.Any<List<string>>(), 1, Arg.Any<CancellationToken>()).Returns(
        [
            Lib(null, null, null, "ENV", "global"),
            Lib(1, null, null, "ENV", "project"),
            Lib(null, 2, null, "ENV", "environment"),
            Lib(null, null, 3, "ENV", "projectserver")
        ]);

        var result = await _sut.ResolveLibrariesWithCrossAccessAsync(["any"], 1, ct: TestContext.Current.CancellationToken);

        Assert.Equal("projectserver", result["ENV"]);
    }

    [Fact]
    public async Task ResolveWithCrossAccess_DistinctKeys_AllPresent_CaseInsensitive()
    {
        _repo.FindByNamesWithCrossAccessAsync(Arg.Any<List<string>>(), 1, Arg.Any<CancellationToken>()).Returns(
        [
            Lib(null, null, null, "A", "1"),
            Lib(1, null, null, "B", "2")
        ]);

        var result = await _sut.ResolveLibrariesWithCrossAccessAsync(["any"], 1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("1", result["a"]); // OrdinalIgnoreCase comparer
    }
}
