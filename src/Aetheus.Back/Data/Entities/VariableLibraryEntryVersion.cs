// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class VariableLibraryEntryVersion
{
    public int Id { get; set; }
    public int VariableLibraryEntryId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime ChangedAt { get; set; }
    public ChangeType ChangeType { get; set; }

    // Navigation
    public VariableLibraryEntry? VariableLibraryEntry { get; set; }
}
