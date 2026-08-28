// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards the two global interaction contracts: static button icons define their visual
/// family, and grids consistently expose inline filtering, sorting and an honest row destination.</summary>
public class ButtonFamilyAndGridNavigationAuditTests
{
    private static readonly IReadOnlyDictionary<string, (string Style, string Variant)> ButtonFamilies = BuildFamilies();

    [Fact]
    public void Every_StaticButtonIcon_UsesItsDesignFamily()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateButtonTags())
        {
            var icon = Attribute(tag, "Icon");
            if (string.IsNullOrWhiteSpace(icon) || icon.StartsWith('@')) continue;
            if (!ButtonFamilies.TryGetValue(icon, out var family))
            {
                violations.Add($"{file}: Icon=\"{icon}\" is not assigned to a button family");
                continue;
            }

            if (icon == "undo" && IsRollbackAction(tag))
                family = ("Danger", "Filled");

            var style = Attribute(tag, "ButtonStyle");
            var variant = Attribute(tag, "Variant");
            // Stateful controls may select one dimension at runtime. The static dimension must
            // still match its semantic family; the runtime dimension is verified by component tests.
            var dynamicStyle = style?.StartsWith('@') == true;
            var dynamicVariant = variant?.StartsWith('@') == true;
            if (dynamicStyle || dynamicVariant)
            {
                if (!dynamicStyle && !string.Equals(style, $"ButtonStyle.{family.Style}", StringComparison.Ordinal)
                    || !dynamicVariant && !string.Equals(variant, $"Variant.{family.Variant}", StringComparison.Ordinal))
                    violations.Add($"{file}: static dimension of dynamic {icon} button must match {family.Style}/{family.Variant}");
                continue;
            }

            if (!string.Equals(style, $"ButtonStyle.{family.Style}", StringComparison.Ordinal)
                || !string.Equals(variant, $"Variant.{family.Variant}", StringComparison.Ordinal))
            {
                violations.Add($"{file}: {icon} must be {family.Style}/{family.Variant}");
            }
        }

        Assert.True(violations.Count == 0,
            "Every static Radzen button icon must use the shared semantic family:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void ClickableGridClass_AlwaysHasARowDestination()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRawGridTags())
        {
            if (!tag.Contains("aetheus-clickable-rows", StringComparison.Ordinal)) continue;
            if (!tag.Contains("RowClick=", StringComparison.Ordinal)) violations.Add(file);
        }

        Assert.True(violations.Count == 0,
            "A grid must not advertise clickable rows without a primary RowClick destination:\n  "
            + string.Join("\n  ", violations.Distinct()));
    }

    [Fact]
    public void EveryRawGrid_ExposesSortingAndInlineFilters()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRawGridTags())
        {
            var sorting = Attribute(tag, "AllowSorting");
            var filtering = Attribute(tag, "AllowFiltering");
            var filterMode = Attribute(tag, "FilterMode");
            if (sorting is not "true" and not "@AllowSorting"
                || filtering is not "true" and not "@AllowFiltering"
                || filterMode is not "FilterMode.Advanced" and not "@FilterMode")
                violations.Add(file);
        }

        Assert.True(violations.Count == 0,
            "Every data grid must keep sorting and advanced inline header filters enabled:\n  "
            + string.Join("\n  ", violations.Distinct()));
    }

    [Fact]
    public void EverySharedGrid_UsesAdvancedDefaultsOrADocumentedServerConstraint()
    {
        var documentedFilterExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pages/Organizations/Organizations.razor",
            "Pages/Notifications/NotificationsAdmin.razor",
            "Pages/PackageRegistry/PackageRegistryAdmin.razor",
            "Pages/PackageFeeds/PackageFeedsAdmin.razor",
            "Pages/Pipelines/PipelineEdit.razor",
            "Pages/Servers/ServerDetailSections/ModuleLinksTab.razor"
        };
        var documentedSortExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pages/Pipelines/PipelineEdit.razor"
        };
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateSharedGridTags())
        {
            if (string.Equals(Attribute(tag, "AllowSorting"), "false", StringComparison.Ordinal)
                && !documentedSortExceptions.Contains(file))
                violations.Add($"{file}: sorting disabled without a documented server constraint");
            if (string.Equals(Attribute(tag, "AllowFiltering"), "false", StringComparison.Ordinal)
                && !documentedFilterExceptions.Contains(file))
                violations.Add($"{file}: filtering disabled without a documented server constraint");
        }

        var sharedSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "Shared", "AetheusDataGrid.razor.cs"));
        Assert.Contains("public bool AllowSorting { get; set; } = true;", sharedSource);
        Assert.Contains("public bool AllowFiltering { get; set; } = true;", sharedSource);
        Assert.Contains("public FilterMode FilterMode { get; set; } = FilterMode.Advanced;", sharedSource);
        Assert.True(violations.Count == 0,
            "Shared grids must keep sorting and advanced filtering unless their paged endpoint cannot express them:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryRawGrid_UsesTheSharedCompactRowDensity()
    {
        var violations = new List<string>();

        foreach (var (file, tag) in EnumerateRawGridTags())
        {
            var density = Attribute(tag, "Density");
            if (density is not "Density.Compact" and not "@Density")
                violations.Add(file);
        }

        Assert.True(violations.Count == 0,
            "Every Radzen data grid must use the shared compact row density:\n  "
            + string.Join("\n  ", violations.Distinct()));
    }

    [Fact]
    public void EveryDataGridLink_UsesTheSharedPipelineLinkStyle()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".pipeline-name-link,\n.server-name-link,\n.rz-datatable .rz-data-row a:not(.rz-button)", css);
        Assert.Contains(".rz-datatable .rz-data-row a:not(.rz-button):hover", css);
    }

    [Fact]
    public void RollbackActions_UseTheDestructiveFamily()
    {
        var rollbackActions = EnumerateButtonTags()
            .Where(item => string.Equals(Attribute(item.Tag, "Icon"), "undo", StringComparison.Ordinal))
            .Where(item => IsRollbackAction(item.Tag))
            .ToList();

        Assert.True(rollbackActions.Count >= 3,
            $"Expected the release rollback actions to be present, found {rollbackActions.Count}.");

        var violations = rollbackActions
            .Where(item => !string.Equals(Attribute(item.Tag, "ButtonStyle"), "ButtonStyle.Danger", StringComparison.Ordinal)
                           || !string.Equals(Attribute(item.Tag, "Variant"), "Variant.Filled", StringComparison.Ordinal))
            .Select(item => $"{item.File}: rollback action must be Danger/Filled: {item.Tag}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Rollback changes deployed state and must use the destructive button family:\n  "
            + string.Join("\n  ", violations));
    }

    private static IReadOnlyDictionary<string, (string Style, string Variant)> BuildFamilies()
    {
        var map = new Dictionary<string, (string Style, string Variant)>();
        Add(map, "Primary", "Filled", "add", "add_circle", "add_link", "save", "check", "check_circle_outline", "create", "person_add", "group_add", "done_all", "login");
        Add(map, "Success", "Filled", "play_arrow", "play_circle", "replay", "rocket_launch", "publish", "build", "send", "restart_alt", "cloud_upload", "install_desktop");
        Add(map, "Danger", "Filled", "delete", "delete_forever", "delete_sweep", "remove", "block", "cancel", "stop", "remove_circle", "person_remove", "logout", "key_off", "link_off");
        Add(map, "Info", "Filled", "edit", "settings", "tune", "manage_accounts", "construction", "open_with");
        Add(map, "Light", "Filled", "upload", "upload_file", "download", "file_download", "cloud_download", "system_update_alt", "check_circle", "format_align_left", "science", "visibility", "preview", "article", "auto_fix_high", "autorenew", "bar_chart", "campaign", "cell_tower", "center_focus_strong", "checklist", "cleaning_services", "description", "dns", "folder_open", "gpp_good", "gpp_maybe", "group", "inventory_2", "key", "link", "lock_open", "lock_reset", "merge_type", "message", "open_in_new", "person", "person_search", "receipt_long", "report_problem", "security", "terminal", "verified_user", "vpn_key", "wifi_tethering");
        Add(map, "Warning", "Text", "star");
        Add(map, "Light", "Text", "arrow_back", "arrow_downward", "arrow_forward", "arrow_upward", "bug_report", "clear", "close", "content_copy", "copy_all", "crisis_alert", "difference", "expand_more", "fiber_new", "fit_screen", "grid_view", "help", "history", "home", "info", "label", "priority_high", "redo", "refresh", "schedule", "search", "swap_horiz", "sync", "undo", "verified", "vertical_align_bottom", "vertical_align_top", "view_column", "view_list", "wrap_text", "zoom_in", "zoom_out");
        return map;
    }

    private static void Add(IDictionary<string, (string Style, string Variant)> map, string style, string variant, params string[] icons)
    {
        foreach (var icon in icons) map.Add(icon, (style, variant));
    }

    private static IEnumerable<(string File, string Tag)> EnumerateButtonTags()
    {
        var front = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        foreach (var file in RepositoryScan.Enumerate(front, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"<Radzen(?:Split)?Button\b(?:(?!/>).)*?/>", RegexOptions.Singleline))
                yield return (Path.GetRelativePath(front, file), match.Value);
        }
    }

    private static IEnumerable<(string File, string Tag)> EnumerateRawGridTags()
    {
        var front = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        foreach (var file in RepositoryScan.Enumerate(front, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"<RadzenDataGrid\b(?:(?:""[^""]*"")|[^>])*?>", RegexOptions.Singleline))
                yield return (Path.GetRelativePath(front, file), match.Value);
        }
    }

    private static IEnumerable<(string File, string Tag)> EnumerateSharedGridTags()
    {
        var front = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        foreach (var file in RepositoryScan.Enumerate(front, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source,
                         @"<(?:Aetheus\.Front\.Shared\.)?AetheusDataGrid\b(?:(?:""[^""]*"")|[^>])*?>",
                         RegexOptions.Singleline))
                yield return (Path.GetRelativePath(front, file).Replace('\\', '/'), match.Value);
        }
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{Regex.Escape(name)}\s*=\s*""(?<v>[^""]*)""");
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static bool IsRollbackAction(string tag) =>
        tag.Contains("Rollback", StringComparison.OrdinalIgnoreCase);

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
