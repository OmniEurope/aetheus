// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

// P: queue-time run-parameters dialog. Returns the effective name→value map (string-encoded) via
// DialogService.Close, or null on cancel. Server-side validation is authoritative; the dialog only
// gathers input and enforces a light required-field check.
public partial class RunParametersDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public List<PipelineRunParameterDto> Parameters { get; set; } = [];

    // S-UX-K7P2: optional seed values (e.g. the source run's parameters on re-run) that override the
    // declared defaults so the dialog opens prefilled with what the previous run used.
    [Parameter] public IReadOnlyDictionary<string, string>? Prefill { get; set; }
    [Parameter] public string? SubmitText { get; set; }
    [Parameter] public string SubmitIcon { get; set; } = "play_arrow";

    private readonly Dictionary<string, string?> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal?> _numbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.OrdinalIgnoreCase);
    private string? _error;

    private bool BoolValue(string name) => _bools.GetValueOrDefault(name);
    private string? StringValue(string name) => _strings.GetValueOrDefault(name);
    private decimal? NumberValue(string name) => _numbers.GetValueOrDefault(name);

    protected override void OnInitialized()
    {
        foreach (var p in Parameters)
        {
            // Prefer the prefill value (source-run snapshot) over the declared default.
            var seed = Prefill is not null && Prefill.TryGetValue(p.Name, out var pv) ? pv : p.Default;
            switch (p.Type?.ToLowerInvariant())
            {
                case "boolean":
                    _bools[p.Name] = bool.TryParse(seed, out var b) && b;
                    break;
                case "number":
                    _numbers[p.Name] = decimal.TryParse(seed, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                        ? d : null;
                    break;
                default:
                    _strings[p.Name] = seed;
                    break;
            }
        }
    }

    private void OnSubmit()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Parameters)
        {
            var value = p.Type?.ToLowerInvariant() switch
            {
                "boolean" => (_bools.TryGetValue(p.Name, out var b) && b) ? "true" : "false",
                "number" => _numbers.TryGetValue(p.Name, out var d) && d.HasValue
                    ? d.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                _ => _strings.TryGetValue(p.Name, out var s) ? s ?? string.Empty : string.Empty
            };

            if (p.Required && string.IsNullOrWhiteSpace(value))
            {
                _error = string.Format(L["RunParameterRequired"], p.DisplayName);
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
                result[p.Name] = value;
        }

        Dialog.Close(result);
    }

    private void OnCancel() => Dialog.Close(null);
}
