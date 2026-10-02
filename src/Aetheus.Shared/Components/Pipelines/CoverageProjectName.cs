// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// The project a covered file belongs to, used to group the coverage report by project (recette R-428).
/// It is the Cobertura package the parser recorded (<see cref="CoverageFileDto.Assembly"/>) when there is
/// one. A report without package names, or a file list stored before the parser recorded them, carries
/// none: the project is then read from the file's path, the directory under the last <c>src</c> or
/// <c>tests</c> segment (<c>src/Aetheus.Back/Components/A.cs</c> gives <c>Aetheus.Back</c>), else the
/// path's first directory, and <c>/</c> for a file at the root. The API and the page share this rule, so
/// the per-project table and the per-project tree always group the same way.
/// </summary>
public static class CoverageProjectName
{
    public const string Root = "/";

    private static readonly char[] Separators = ['/', '\\'];

    public static string Of(CoverageFileDto file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!string.IsNullOrWhiteSpace(file.Assembly)) return file.Assembly;

        var segments = file.File.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var directories = segments.Length - 1;
        if (directories <= 0) return Root;

        for (var index = directories - 2; index >= 0; index--)
        {
            if (IsSourceRoot(segments[index])) return segments[index + 1];
        }

        return segments[0];
    }

    private static bool IsSourceRoot(string segment) =>
        segment.Equals("src", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("test", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("tests", StringComparison.OrdinalIgnoreCase);
}
