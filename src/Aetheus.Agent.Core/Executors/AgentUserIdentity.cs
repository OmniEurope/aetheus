// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Aetheus.Agent.Core.Executors;

/// <summary>
/// S-TECH-72: the agent's real uid/gid, passed to <c>docker run --user</c> by the toolchain and scanner
/// containers so their output is owned by the agent rather than root. Linux-only (container isolation
/// is a Linux feature); callers pick another identity on Windows.
/// </summary>
internal static class AgentUserIdentity
{
    internal static uint Uid => getuid();

    internal static uint Gid => getgid();

    // CA5392 asks for DefaultDllImportSearchPaths, which selects among WINDOWS DLL search paths and
    // has no effect on the Unix loader. These calls are Linux-only by construction (container isolation
    // is a Linux feature), so the attribute would document a protection that is not there.
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes",
        Justification = "libc on Linux; the attribute only constrains the Windows DLL search order.")]
    [DllImport("libc", SetLastError = false)]
    private static extern uint getuid();

    // CA5392 asks for DefaultDllImportSearchPaths, which selects among WINDOWS DLL search paths and
    // has no effect on the Unix loader. These calls are Linux-only by construction (container isolation
    // is a Linux feature), so the attribute would document a protection that is not there.
    [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes",
        Justification = "libc on Linux; the attribute only constrains the Windows DLL search order.")]
    [DllImport("libc", SetLastError = false)]
    private static extern uint getgid();
}
