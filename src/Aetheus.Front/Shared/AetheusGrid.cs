// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

/// <summary>
/// Product-wide switch for grid row virtualization. Off since 2026-08-23: every table pages.
/// </summary>
/// <remarks>
/// Every table pages rather than scrolling infinitely. Virtualization was the default until a reader
/// hit what it costs: Radzen can only virtualise inside a bounded height, so the grid grew its own
/// scroll body while the page kept its own, and a single table carried two scrollbars. Paging and
/// virtualization are mutually exclusive in Radzen, so the pager is the actual fix.
/// <para>
/// This is a switch rather than a constant because it stays useful in the other direction: a view
/// with a genuinely unbounded row count can opt back in per grid, and the test host can flip it
/// globally. Under bUnit there is no layout and no real JS runtime, so a virtualised grid measures a
/// zero-height viewport and renders no rows at all, which would fail render tests for a reason that
/// has nothing to do with the component.
/// </para>
/// </remarks>
public static class AetheusGrid
{
    /// <summary>False since 2026-08-23: tables page. Never branch on this in product logic, it decides
    /// how rows are rendered, never what they contain.</summary>
    public static bool Virtualization { get; set; }
}
