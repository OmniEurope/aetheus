// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// The on-disk transaction journal, and the reason the blue-green sequence can be split across
/// separate pipeline steps at all.
///
/// The shell implementations held their environment lease as a file lock inside one long-lived
/// process, which forced the whole cutover into a single opaque step. A lock cannot cross a step
/// boundary, but a journal can: each step reads the state its predecessor committed, so an
/// interrupted deployment is reconcilable by whatever runs next instead of being lost with the
/// process that died.
///
/// Every write is atomic (temp file then rename) so a crash mid-write can never leave a half-written
/// state that a later step would misread.
/// </summary>
internal sealed class BlueGreenJournal(BlueGreenContext context)
{
    /// <summary>The idle colour is prepared but nothing user-visible has changed yet.</summary>
    internal const string Prepared = "PREPARED";

    /// <summary>Traffic points at the new colour. The previous colour is still running and can take it back.</summary>
    internal const string Switched = "SWITCHED";

    /// <summary>The deployment is final. The journal is cleared immediately after this is written.</summary>
    internal const string Committed = "COMMITTED";

    private string StatePath => Path.Combine(context.JournalDir, "state");
    private string PreviousPath => Path.Combine(context.JournalDir, "previous-live");
    private string IdlePath => Path.Combine(context.JournalDir, "idle");
    private string RevisionPath => Path.Combine(context.JournalDir, "revision");

    internal bool Exists => Directory.Exists(context.JournalDir);

    internal string? State => ReadOrNull(StatePath);
    internal string? PreviousLive => ReadOrNull(PreviousPath);
    internal string? Idle => ReadOrNull(IdlePath);
    internal string? Revision => ReadOrNull(RevisionPath);

    /// <summary>Reads the colour currently serving traffic, or null when none has been recorded yet.</summary>
    internal string? ReadLiveColour()
    {
        var colour = ReadOrNull(context.ColorFile);
        return colour is "blue" or "green" ? colour : null;
    }

    internal void WriteLiveColour(string colour) => WriteAtomic(context.ColorFile, colour);

    internal void WriteDeployedRevision(string revision) => WriteAtomic(context.SourceCommitFile, revision);

    /// <summary>
    /// Opens a transaction. Refuses to overwrite an unfinished one: a second deployment that
    /// silently replaced a live journal would destroy the only record of how to get back.
    /// </summary>
    internal bool TryOpen(string previousLive, string idle, string revision, out string error)
    {
        error = string.Empty;
        // A directory with no `state` file at all is not a transaction. TryOpen writes `state` last,
        // so anything that stopped before it left the upstream configuration and both colours
        // untouched: there is nothing to protect, and refusing it wedged the environment for good,
        // because nothing in the product removes a journal it will not read. Seen on the mirror:
        // every deployment refused with "state=unknown" until the empty directory was removed by hand.
        // The file must be ABSENT, not merely unreadable - ReadOrNull returns null for both, and an
        // unreadable state file is a real transaction whose record we cannot see, which must still be
        // refused rather than silently replaced.
        if (Exists && File.Exists(StatePath) && State != Committed)
        {
            error = $"An unfinished deployment transaction is present (state={State ?? "unknown"}); "
                + "resolve it before starting another deployment.";
            return false;
        }
        Clear();
        Directory.CreateDirectory(context.JournalDir);
        HardenDirectory(context.JournalDir);
        WriteAtomic(PreviousPath, previousLive);
        WriteAtomic(IdlePath, idle);
        WriteAtomic(RevisionPath, revision);
        WriteAtomic(StatePath, Prepared);
        return true;
    }

    internal void MarkSwitched() => WriteAtomic(StatePath, Switched);

    internal void Commit()
    {
        WriteAtomic(StatePath, Committed);
        Clear();
    }

    internal void Clear()
    {
        if (Directory.Exists(context.JournalDir)) Directory.Delete(context.JournalDir, recursive: true);
    }

    /// <summary>
    /// Forgets which colour serves traffic. Only a retirement does this: it is the one outcome that
    /// leaves no colour serving at all, and a stale record would make the next deployment believe it
    /// has a live predecessor to fall back to.
    /// </summary>
    internal void ClearLiveColour()
    {
        try
        {
            if (File.Exists(context.ColorFile)) File.Delete(context.ColorFile);
        }
        catch (IOException)
        {
            // Best effort: the transaction is already undone, and ReadLiveColour treats an
            // unreadable file the same way it treats an absent one.
        }
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = $"{path}.tmp.{Environment.ProcessId}";
        File.WriteAllText(temp, content + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }

    private static void HardenDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
