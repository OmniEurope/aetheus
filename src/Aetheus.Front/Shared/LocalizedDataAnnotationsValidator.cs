// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;

namespace Aetheus.Front.Shared;

/// <summary>
/// Drop-in replacement for <see cref="DataAnnotationsValidator"/> that translates the standard
/// English error messages emitted by <c>System.ComponentModel.DataAnnotations</c> using the
/// app's <see cref="IStringLocalizer{AppStrings}"/>. Resolves audit findings F-34 / F-39.
/// </summary>
/// <remarks>
/// Uses regex-based recognition of the well-known default messages so that we don't need to
/// touch every DTO with <c>ErrorMessageResourceType</c>. Custom <c>ErrorMessage</c>-bearing
/// attributes are passed through unchanged.
/// </remarks>
public sealed class LocalizedDataAnnotationsValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private ValidationMessageStore? _messages;
    private EventHandler<ValidationRequestedEventArgs>? _validationRequestedHandler;
    private EventHandler<FieldChangedEventArgs>? _fieldChangedHandler;

    private static readonly Regex RequiredRegex =
        new(@"^The (?<f>.+) field is required\.$", RegexOptions.Compiled);
    private static readonly Regex MaxLenRegex =
        new(@"^The field (?<f>.+) must be a string with a maximum length of (?<max>\d+)\.$", RegexOptions.Compiled);
    private static readonly Regex StringLenRegex =
        new(@"^The field (?<f>.+) must be a string with a minimum length of (?<min>\d+) and a maximum length of (?<max>\d+)\.$", RegexOptions.Compiled);
    private static readonly Regex RangeRegex =
        new(@"^The field (?<f>.+) must be between (?<min>.+) and (?<max>.+)\.$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex =
        new(@"^The (?<f>.+) field is not a valid e-mail address\.$", RegexOptions.Compiled);
    private static readonly Regex UrlRegex =
        new(@"^The (?<f>.+) field is not a valid fully-qualified http, https, or ftp URL\.$", RegexOptions.Compiled);
    private static readonly Regex CompareRegex =
        new(@"^'(?<f>.+)' and '(?<o>.+)' do not match\.$", RegexOptions.Compiled);

    protected override void OnInitialized()
    {
        ArgumentNullException.ThrowIfNull(CurrentEditContext);
        _messages = new ValidationMessageStore(CurrentEditContext);
        _validationRequestedHandler = (_, _) => ValidateAll();
        _fieldChangedHandler = (_, args) => ValidateField(args.FieldIdentifier);
        CurrentEditContext.OnValidationRequested += _validationRequestedHandler;
        CurrentEditContext.OnFieldChanged += _fieldChangedHandler;
    }

    private void ValidateAll()
    {
        if (CurrentEditContext is null || _messages is null) return;
        _messages.Clear();
        var ctx = new ValidationContext(CurrentEditContext.Model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(CurrentEditContext.Model, ctx, results, validateAllProperties: true);
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [string.Empty];
            foreach (var member in members)
            {
                _messages.Add(new FieldIdentifier(CurrentEditContext.Model, member), Translate(result.ErrorMessage));
            }
        }
        CurrentEditContext.NotifyValidationStateChanged();
    }

    private void ValidateField(FieldIdentifier field)
    {
        if (CurrentEditContext is null || _messages is null) return;
        var prop = field.Model.GetType().GetProperty(field.FieldName);
        if (prop is null) return;

        _messages.Clear(field);
        var value = prop.GetValue(field.Model);
        var attrs = prop.GetCustomAttributes(inherit: true).OfType<ValidationAttribute>().ToArray();
        var ctx = new ValidationContext(field.Model) { MemberName = field.FieldName };
        var results = new List<ValidationResult>();
        Validator.TryValidateValue(value!, ctx, results, attrs);
        foreach (var result in results)
        {
            _messages.Add(field, Translate(result.ErrorMessage));
        }
        CurrentEditContext.NotifyValidationStateChanged();
    }

    private string Translate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        Match m;
        if ((m = RequiredRegex.Match(raw)).Success)
            return L["Validation_Required", LocalizeFieldName(m.Groups["f"].Value)];
        if ((m = StringLenRegex.Match(raw)).Success)
            return L["Validation_StringLength", LocalizeFieldName(m.Groups["f"].Value), m.Groups["min"].Value, m.Groups["max"].Value];
        if ((m = MaxLenRegex.Match(raw)).Success)
            return L["Validation_MaxLength", LocalizeFieldName(m.Groups["f"].Value), m.Groups["max"].Value];
        if ((m = RangeRegex.Match(raw)).Success)
            return L["Validation_Range", LocalizeFieldName(m.Groups["f"].Value), m.Groups["min"].Value, m.Groups["max"].Value];
        if ((m = EmailRegex.Match(raw)).Success)
            return L["Validation_Email", LocalizeFieldName(m.Groups["f"].Value)];
        if ((m = UrlRegex.Match(raw)).Success)
            return L["Validation_Url", LocalizeFieldName(m.Groups["f"].Value)];
        if ((m = CompareRegex.Match(raw)).Success)
            return L["Validation_Compare", LocalizeFieldName(m.Groups["f"].Value), LocalizeFieldName(m.Groups["o"].Value)];
        // A custom ErrorMessage that is a bare resource key (e.g. "SlugFormatError") is localized via
        // the resx; anything else (a full sentence) is shown as-is.
        var custom = L[raw];
        return custom.ResourceNotFound ? raw : custom.Value;
    }

    /// <summary>
    /// Best-effort field-name translation: tries the localizer using the field name as key
    /// (so "Name" → "Nom" if a resource exists) and falls back to the raw name otherwise.
    /// </summary>
    private string LocalizeFieldName(string name)
    {
        var localized = L[name];
        return localized.ResourceNotFound ? name : localized.Value;
    }

    public void Dispose()
    {
        if (CurrentEditContext is not null)
        {
            if (_validationRequestedHandler is not null)
                CurrentEditContext.OnValidationRequested -= _validationRequestedHandler;
            if (_fieldChangedHandler is not null)
                CurrentEditContext.OnFieldChanged -= _fieldChangedHandler;
        }
    }
}
