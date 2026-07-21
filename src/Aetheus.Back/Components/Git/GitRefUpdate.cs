// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Git;

/// <summary>A branch update requested by the client and confirmed by receive-pack.</summary>
public sealed record GitRefUpdate(string Reference, string NewObjectId);
