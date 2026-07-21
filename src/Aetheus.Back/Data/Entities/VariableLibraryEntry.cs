// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class VariableLibraryEntry
{
    public int Id { get; set; }
    public int VariableLibraryId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    // Navigation
    public VariableLibrary VariableLibrary { get; set; } = null!;
    public List<VariableLibraryEntryVersion> Versions { get; set; } = [];
}
