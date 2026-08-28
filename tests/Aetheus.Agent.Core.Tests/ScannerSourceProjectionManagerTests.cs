// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class ScannerSourceProjectionManagerTests
{
    [Theory]
    [InlineData(".pipeline-artifacts/archive.tar")]
    [InlineData("src/App/bin/Release/App.dll")]
    [InlineData("src/App/obj/project.assets.json")]
    [InlineData("coverage/report.xml")]
    [InlineData("web/node_modules/pkg/index.js")]
    public void Projection_ExcludesGeneratedOutputs(string path)
    {
        Assert.True(ScannerSourceStager.ShouldExcludeFromProjection(path));
    }

    [Theory]
    [InlineData(".git/objects/ab/cdef")]
    [InlineData("src/App/App.cs")]
    [InlineData(".analysis-image/aetheus-back.tar")]
    [InlineData(".analysis-image/aetheus-front.tar")]
    public void Projection_KeepsGitAndRequiredSourceEvidence(string path)
    {
        Assert.False(ScannerSourceStager.ShouldExcludeFromProjection(path));
    }

    [Fact]
    public void DependencyAwareProjection_KeepsInstalledLockedPackages()
    {
        Assert.False(ScannerSourceStager.ShouldExcludeFromProjection(
            "node_modules/jscpd-linux-x64-musl/bin/jscpd",
            includeNodeModules: true));
    }

    [Fact]
    public async Task MultipleScannersReuseOneImmutableProjection_ThenCleanItOnce()
    {
        var source = Directory.CreateTempSubdirectory("aetheus-projection-source-").FullName;
        var work = Directory.CreateTempSubdirectory("aetheus-projection-work-").FullName;
        try
        {
            var sourceFile = Path.Combine(source, "source.cs");
            await File.WriteAllTextAsync(sourceFile, "original", TestContext.Current.CancellationToken);
            var manager = new ScannerSourceProjectionManager(TimeSpan.FromMilliseconds(25));

            await using var first = await manager.AcquireAsync(
                42, source, work, includeLockedDependencies: false, TestContext.Current.CancellationToken);
            await using var second = await manager.AcquireAsync(
                42, source, work, includeLockedDependencies: false, TestContext.Current.CancellationToken);

            Assert.Equal(first.SourceDirectory, second.SourceDirectory);
            Assert.Equal(1, manager.StagingCount);
            await File.WriteAllTextAsync(sourceFile, "hostile-later-write", TestContext.Current.CancellationToken);
            Assert.Equal("original", await File.ReadAllTextAsync(
                Path.Combine(first.SourceDirectory, "source.cs"),
                TestContext.Current.CancellationToken));

            await first.DisposeAsync();
            Assert.True(Directory.Exists(first.SourceDirectory));
            await second.DisposeAsync();
            await WaitForAsync(() => manager.ActiveProjectionCount == 0, TimeSpan.FromSeconds(2));

            Assert.False(Directory.Exists(first.SourceDirectory));
            Assert.Equal(1, manager.StagingCount);
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public async Task ScannerAcquiredAfterLastLease_RestagesPipelineWorkspace()
    {
        var source = Directory.CreateTempSubdirectory("aetheus-projection-source-").FullName;
        var work = Directory.CreateTempSubdirectory("aetheus-projection-work-").FullName;
        try
        {
            var sourceFile = Path.Combine(source, "source.cs");
            await File.WriteAllTextAsync(sourceFile, "before-stage", TestContext.Current.CancellationToken);
            var manager = new ScannerSourceProjectionManager(TimeSpan.FromSeconds(30));

            string firstProjection;
            await using (var first = await manager.AcquireAsync(
                             42, source, work, includeLockedDependencies: false,
                             TestContext.Current.CancellationToken))
            {
                firstProjection = first.SourceDirectory;
                Assert.Equal("before-stage", await File.ReadAllTextAsync(
                    Path.Combine(first.SourceDirectory, "source.cs"),
                    TestContext.Current.CancellationToken));
            }

            await File.WriteAllTextAsync(sourceFile, "after-stage", TestContext.Current.CancellationToken);
            var imageDirectory = Directory.CreateDirectory(Path.Combine(source, ".analysis-image"));
            await File.WriteAllTextAsync(
                Path.Combine(imageDirectory.FullName, "source-commit"),
                "abcdef12",
                TestContext.Current.CancellationToken);

            await using var second = await manager.AcquireAsync(
                42, source, work, includeLockedDependencies: false, TestContext.Current.CancellationToken);

            Assert.Equal(firstProjection, second.SourceDirectory);
            Assert.Equal(2, manager.StagingCount);
            Assert.Equal("after-stage", await File.ReadAllTextAsync(
                Path.Combine(second.SourceDirectory, "source.cs"),
                TestContext.Current.CancellationToken));
            Assert.Equal("abcdef12", await File.ReadAllTextAsync(
                Path.Combine(second.SourceDirectory, ".analysis-image", "source-commit"),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public async Task LockedDependencyScanner_ProjectsInstalledPackages()
    {
        var source = Directory.CreateTempSubdirectory("aetheus-projection-source-").FullName;
        var work = Directory.CreateTempSubdirectory("aetheus-projection-work-").FullName;
        try
        {
            var packageDirectory = Directory.CreateDirectory(Path.Combine(source, "node_modules", "jscpd"));
            await File.WriteAllTextAsync(
                Path.Combine(packageDirectory.FullName, "package.json"),
                "{\"name\":\"jscpd\",\"version\":\"5.0.12\"}",
                TestContext.Current.CancellationToken);
            var nativeBinDirectory = Directory.CreateDirectory(Path.Combine(
                source,
                "node_modules",
                "jscpd-linux-x64-musl",
                "bin"));
            await File.WriteAllTextAsync(
                Path.Combine(nativeBinDirectory.FullName, "jscpd"),
                "native-binary",
                TestContext.Current.CancellationToken);
            var manager = new ScannerSourceProjectionManager(TimeSpan.FromSeconds(30));

            await using var lease = await manager.AcquireAsync(
                42, source, work, includeLockedDependencies: true, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(
                lease.SourceDirectory,
                "node_modules",
                "jscpd",
                "package.json")));
            Assert.True(File.Exists(Path.Combine(
                lease.SourceDirectory,
                "node_modules",
                "jscpd-linux-x64-musl",
                "bin",
                "jscpd")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Projection cleanup did not complete in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
