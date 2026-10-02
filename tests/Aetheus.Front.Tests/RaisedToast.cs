// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests;

/// <summary>One toast as NotifyHelper was asked for it: its severity, summary and detail
/// (read back by <see cref="OmniToastAssertions"/>).</summary>
internal readonly record struct RaisedToast(OmniSeverity Severity, string Summary, string Detail);
