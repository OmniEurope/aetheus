// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Unit coverage for the pure deploy filesystem helpers - the zip-slip guard, run-launcher discovery,
/// release pruning, free-space gate and systemd state parser. These run off-Linux (no shell, no
/// systemd), closing the "DeployOperationExecutor has zero tests" gap on the security-critical bits.
/// </summary>
public sealed class DeployLayoutTests
{
    // ── IsSafeRelativePath ───────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("docker-compose.yml", true)]
    [InlineData("sub/dir/compose.yml", true)]
    [InlineData("../escape.yml", false)]
    [InlineData("a/../../b.yml", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSafeRelativePath_classifies(string path, bool expected)
        => Assert.Equal(expected, DeployLayout.IsSafeRelativePath(path));

    // ── ResolveSafeEntryPath (zip-slip) ──────────────────────────────────────────────────────────
    [Fact]
    public void ResolveSafeEntryPath_allows_in_tree_file()
    {
        var dest = Path.Combine(Path.GetTempPath(), "prom-zs");
        var resolved = DeployLayout.ResolveSafeEntryPath(dest, "app/run");
        Assert.NotNull(resolved);
        Assert.StartsWith(Path.GetFullPath(dest), resolved);
    }

    [Fact]
    public void ResolveSafeEntryPath_returns_null_for_directory_entry()
        => Assert.Null(DeployLayout.ResolveSafeEntryPath(Path.GetTempPath(), "app/"));

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../../../etc/passwd")]
    public void ResolveSafeEntryPath_throws_on_escape(string entry)
        => Assert.Throws<InvalidDataException>(() => DeployLayout.ResolveSafeEntryPath(Path.Combine(Path.GetTempPath(), "prom-zs"), entry));

    [Fact]
    public void ExtractSafely_writes_nested_entries()
    {
        var dir = NewTempDir();
        try
        {
            var zipPath = Path.Combine(dir, "a.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "publish/toto/run", "#!/bin/sh\n");
                WriteEntry(zip, "publish/toto/app.dll", "x");
            }
            var outDir = Path.Combine(dir, "out");
            Directory.CreateDirectory(outDir);
            using (var archive = ZipFile.OpenRead(zipPath))
                DeployLayout.ExtractSafely(archive, outDir);

            Assert.True(File.Exists(Path.Combine(outDir, "publish", "toto", "run")));
            Assert.True(File.Exists(Path.Combine(outDir, "publish", "toto", "app.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateArchive_rejects_symbolic_link_entries()
    {
        var dir = NewTempDir();
        try
        {
            var zipPath = Path.Combine(dir, "symlink.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("publish/run");
                entry.ExternalAttributes = 0xA000 << 16;
            }
            using var archive = ZipFile.OpenRead(zipPath);

            Assert.Throws<InvalidDataException>(() =>
                DeployLayout.ValidateArchive(archive, Path.Combine(dir, "out")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateArchive_rejects_entry_and_expansion_quotas()
    {
        var dir = NewTempDir();
        try
        {
            var zipPath = Path.Combine(dir, "quota.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "one.txt", "1234");
                WriteEntry(zip, "two.txt", "5678");
            }
            using var archive = ZipFile.OpenRead(zipPath);

            Assert.Throws<InvalidDataException>(() =>
                DeployLayout.ValidateArchive(archive, Path.Combine(dir, "out"), maxEntries: 1));
            Assert.Throws<InvalidDataException>(() =>
                DeployLayout.ValidateArchive(archive, Path.Combine(dir, "out"), maxExpandedBytes: 7));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── ResolveAppRoot (the run-mapping bug) ─────────────────────────────────────────────────────
    [Fact]
    public void ResolveAppRoot_returns_root_when_run_at_root()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "run"), "x");
            Assert.Equal(dir, DeployLayout.ResolveAppRoot(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveAppRoot_finds_nested_run_launcher()
    {
        // The artifact collector keeps the publish/toto/ prefix → run is nested, not at the root.
        var dir = NewTempDir();
        try
        {
            var nested = Path.Combine(dir, "publish", "toto");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "run"), "x");
            Assert.Equal(nested, DeployLayout.ResolveAppRoot(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveAppRoot_null_when_absent()
    {
        var dir = NewTempDir();
        try { Assert.Null(DeployLayout.ResolveAppRoot(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveAppRoot_null_when_ambiguous()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "a"));
            Directory.CreateDirectory(Path.Combine(dir, "b"));
            File.WriteAllText(Path.Combine(dir, "a", "run"), "x");
            File.WriteAllText(Path.Combine(dir, "b", "run"), "x");
            Assert.Null(DeployLayout.ResolveAppRoot(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Release permissions (DynamicUser + aetheus-deploy) ──────────────────────────────────────
    [Fact]
    public void HardenedReleaseFileMode_makes_regular_file_group_readable_without_world_access()
    {
        var mode = DeployReleaseFileSystem.HardenedFileMode(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            mode);
    }

    [Fact]
    public void HardenedReleaseFileMode_preserves_executable_as_owner_and_group_only()
    {
        var mode = DeployReleaseFileSystem.HardenedFileMode(
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);

        Assert.Equal(DeployReleaseFileSystem.ReleaseDirectoryMode, mode);
    }

    // ── TopLevelReleaseDir ───────────────────────────────────────────────────────────────────────
    [Fact]
    public void TopLevelReleaseDir_maps_nested_target_to_release_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "releases");
        var target = Path.Combine(root, "42", "publish", "toto");
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "42")), DeployLayout.TopLevelReleaseDir(target, root));
    }

    [Fact]
    public void TopLevelReleaseDir_maps_direct_target()
    {
        var root = Path.Combine(Path.GetTempPath(), "releases");
        var target = Path.Combine(root, "42");
        Assert.Equal(Path.GetFullPath(target), DeployLayout.TopLevelReleaseDir(target, root));
    }

    [Fact]
    public void TopLevelReleaseDir_null_for_outside_target()
        => Assert.Null(DeployLayout.TopLevelReleaseDir("/somewhere/else", Path.Combine(Path.GetTempPath(), "releases")));

    [Fact]
    public void TopLevelReleaseDir_null_for_empty()
        => Assert.Null(DeployLayout.TopLevelReleaseDir(null, "/r"));

    // ── SelectPrunableReleaseDirs ────────────────────────────────────────────────────────────────
    [Fact]
    public void SelectPrunableReleaseDirs_keeps_newest_and_protects()
    {
        var dirs = new[] { "d5", "d4", "d3", "d2", "d1" }; // newest-first
        var prune = DeployLayout.SelectPrunableReleaseDirs(dirs, keep: 2, new HashSet<string>());
        Assert.Equal(new[] { "d3", "d2", "d1" }, prune);
    }

    [Fact]
    public void SelectPrunableReleaseDirs_never_prunes_protected()
    {
        var dirs = new[] { "d5", "d4", "d3", "d2", "d1" };
        var prune = DeployLayout.SelectPrunableReleaseDirs(dirs, keep: 1, new HashSet<string> { "d1" });
        Assert.DoesNotContain("d1", prune);     // protected (current/previous)
        Assert.Contains("d3", prune);
        Assert.Equal(new[] { "d4", "d3", "d2" }, prune); // d5 kept (newest, budget 1), d1 protected
    }

    // ── HasEnoughFreeSpace ───────────────────────────────────────────────────────────────────────
    [Fact]
    public void HasEnoughFreeSpace_true_with_headroom()
        => Assert.True(DeployLayout.HasEnoughFreeSpace(10L * 1024 * 1024 * 1024, 100L * 1024 * 1024));

    [Fact]
    public void HasEnoughFreeSpace_false_when_tight()
        => Assert.False(DeployLayout.HasEnoughFreeSpace(100L * 1024 * 1024, 200L * 1024 * 1024));

    [Fact]
    public void HasEnoughFreeSpace_requires_floor_even_for_zero_payload()
        => Assert.False(DeployLayout.HasEnoughFreeSpace(DeployLayout.MinFreeSpaceBytes - 1, 0));

    // ── ParseUnitState (crash-loop detection) ────────────────────────────────────────────────────
    [Fact]
    public void ParseUnitState_parses_running()
    {
        var s = DeployLayout.ParseUnitState("ActiveState=active\nSubState=running\nNRestarts=0\n");
        Assert.True(s.IsActiveRunning);
        Assert.Equal(0, s.NRestarts);
    }

    [Fact]
    public void ParseUnitState_detects_crash_loop_counter()
    {
        var s = DeployLayout.ParseUnitState("ActiveState=activating\nSubState=auto-restart\nNRestarts=7");
        Assert.False(s.IsActiveRunning);
        Assert.Equal(7, s.NRestarts);
    }

    [Fact]
    public void ParseUnitState_tolerates_noise_and_order()
    {
        var s = DeployLayout.ParseUnitState("NRestarts=3\nUnrelated=x\nSubState=running\nActiveState=active");
        Assert.True(s.IsActiveRunning);
        Assert.Equal(3, s.NRestarts);
    }

    // ── ResolveHealthWindowSeconds ─────────────────────────────────────────────────────────────────
    [Theory]
    // Derived window (health_timeout = 0): timeout/3, clamped to [20, 120].
    [InlineData(300, 0, 100)]   // 300/3 = 100
    [InlineData(30, 0, 20)]     // 10 → floor 20
    [InlineData(600, 0, 120)]   // 200 → cap 120
    // Explicit health_timeout wins - decoupled from the 120 derived cap, bounded only by the step timeout.
    [InlineData(300, 250, 250)] // honoured well beyond 120 - the whole point of the field
    [InlineData(300, 5, 20)]    // below the floor → 20
    [InlineData(60, 200, 60)]   // cannot outlast the overall step timeout
    public void ResolveHealthWindowSeconds_picks_explicit_or_derived(int timeout, int health, int expected)
    {
        Assert.Equal(expected, DeployLayout.ResolveHealthWindowSeconds(timeout, health));
    }

    // ── EvaluateHealthSample (crash-loop / warmup-then-crash decision) ────────────────────────────

    private static DeployLayout.UnitState Running(int restarts = 0) => new("active", "running", restarts);
    private static DeployLayout.UnitState Activating(int restarts = 0) => new("activating", "start", restarts);

    [Fact]
    public void EvaluateHealthSample_ContinuousRunning_PassesAtRequiredSamples()
    {
        int? baseline = null;
        var stable = 0;
        // 4 required samples: first three Continue, fourth Passes.
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 4));
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 4));
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 4));
        Assert.Equal(DeployLayout.HealthDecision.Pass, DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 4));
    }

    [Fact]
    public void EvaluateHealthSample_RestartsClimb_CrashLooped()
    {
        int? baseline = null;
        var stable = 0;
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(restarts: 2), ref baseline, ref stable, 4));
        // NRestarts climbs above the first-seen baseline (2) -> crash-loop, even while "running".
        Assert.Equal(DeployLayout.HealthDecision.CrashLooped, DeployLayout.EvaluateHealthSample(Running(restarts: 3), ref baseline, ref stable, 4));
    }

    [Fact]
    public void EvaluateHealthSample_WarmupThenCrash_DoesNotFalsePass()
    {
        int? baseline = null;
        var stable = 0;
        // Two early running reads (would false-pass a 2-sample gate) ...
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(restarts: 0), ref baseline, ref stable, 4));
        Assert.Equal(DeployLayout.HealthDecision.Continue, DeployLayout.EvaluateHealthSample(Running(restarts: 0), ref baseline, ref stable, 4));
        // ... then a crash (restart increments): tripped, never reaching 4 stable hits.
        Assert.Equal(DeployLayout.HealthDecision.CrashLooped, DeployLayout.EvaluateHealthSample(Activating(restarts: 1), ref baseline, ref stable, 4));
    }

    [Fact]
    public void EvaluateHealthSample_NonRunningSample_ResetsStableStreak()
    {
        int? baseline = null;
        var stable = 0;
        DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 3);
        DeployLayout.EvaluateHealthSample(Running(), ref baseline, ref stable, 3);
        Assert.Equal(2, stable);
        // A non-running sample (no restart climb) resets the continuous-dwell streak to 0.
        DeployLayout.EvaluateHealthSample(Activating(), ref baseline, ref stable, 3);
        Assert.Equal(0, stable);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prom-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var w = new StreamWriter(entry.Open());
        w.Write(content);
    }
}
