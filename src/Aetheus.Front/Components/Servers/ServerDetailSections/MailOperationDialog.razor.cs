// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

public partial class MailOperationDialog
{
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public MailDialogMode Mode { get; set; }
    [Parameter] public MailDialogModel Model { get; set; } = new();
    [Parameter] public int ServerId { get; set; }
    [Parameter] public IReadOnlyList<MailDomainDto> Domains { get; set; } = [];
    [Parameter] public MailDnsRecordsDto? DnsRecords { get; set; }

    private MailDialogModel _model = new();
    private int _setupStep = 1;
    private MailMxPreviewDto? _mxPreview;
    private bool _checkingMx;
    private bool _mxCheckFailed;
    private string _mxCheckedDomain = string.Empty;
    private bool CanCheckMx => ServerId > 0 && _model.Domain.Length <= 253
        && MailValidation.IsValidDomainName(_model.Domain.Trim());
    private bool PreviewMatchesDomain => _mxPreview is not null
        && _mxPreview.Domain.Equals(_model.Domain.Trim(), StringComparison.OrdinalIgnoreCase);
    private bool MxFailureMatchesDomain => _mxCheckFailed
        && _mxCheckedDomain.Equals(_model.Domain.Trim(), StringComparison.OrdinalIgnoreCase);
    private string SubmitText => Mode switch
    {
        MailDialogMode.Setup => L["Install"],
        MailDialogMode.RotateDkim => L["Rotate"],
        MailDialogMode.RequestCertificate => L["MailObtainCertificate"],
        MailDialogMode.SpamThresholds => L["Save"],
        MailDialogMode.LearnSpam => L["MailLearn"],
        MailDialogMode.TestDelivery => L["Send"],
        _ => L["Add"]
    };
    private IReadOnlyList<DnsRecord> DnsRecordList => DnsRecords is null ? [] :
    [
        new(L["MxRecord"], DnsRecords.MxRecord, false), new("SPF (TXT)", DnsRecords.SpfRecord, false),
        new(L["DkimSelector"], DnsRecords.DkimSelector, false), new($"DMARC (TXT _dmarc.{DnsRecords.Domain})", DnsRecords.DmarcRecord, false),
        new($"DKIM (TXT {DnsRecords.DkimSelector}._domainkey.{DnsRecords.Domain})", DnsRecords.DkimPublicKey, true)
    ];

    // R-511: OmniDialog listens to the keyboard, so every key press re-renders the dialog and hands it the
    // same parameters again. Copying the model there wiped what had just been typed: it is copied once.
    protected override void OnInitialized() => _model = Model.Clone();
    protected override void OnParametersSet() => _model.Mode = Mode;
    private void Submit(MailDialogModel model) => Dialog.Close(model);
    private async Task CheckMxAsync()
    {
        if (!CanCheckMx) return;
        _checkingMx = true;
        _mxCheckFailed = false;
        _mxPreview = null;
        _mxCheckedDomain = _model.Domain.Trim();
        try
        {
            _mxPreview = await Api.Mail.PreviewMailMxAsync(ServerId, _mxCheckedDomain);
            _mxCheckFailed = _mxPreview is null;
        }
        catch (HttpRequestException)
        {
            _mxCheckFailed = true;
        }
        finally
        {
            _checkingMx = false;
        }
    }
    /// <summary>Recette R2-060: the fields each setup step owns. Next used to move on whatever was typed,
    /// so a bare host name or an address outside the domain only failed at the very end.</summary>
    internal static readonly IReadOnlyDictionary<int, string[]> SetupStepMembers = new Dictionary<int, string[]>
    {
        [1] = [nameof(MailDialogModel.Hostname), nameof(MailDialogModel.Domain), nameof(MailDialogModel.DkimSelector)],
        [2] = [nameof(MailDialogModel.Email), nameof(MailDialogModel.Password)]
    };

    private IReadOnlyList<string> _stepErrors = [];

    /// <summary>The model's own validation, kept to the members of one step and translated.</summary>
    internal IReadOnlyList<string> StepErrors(int step)
    {
        if (!SetupStepMembers.TryGetValue(step, out var members)) return [];
        var results = new List<ValidationResult>();
        // Attribute checks first; the model's rules run only when they pass (as EditContext does), so
        // both are read here.
        Validator.TryValidateObject(_model, new ValidationContext(_model), results, validateAllProperties: true);
        results.AddRange(_model.Validate(new ValidationContext(_model)));
        return [.. results
            .Where(result => result.MemberNames.Any(members.Contains))
            .Select(result => L[result.ErrorMessage ?? "Required"].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    private void NextStep()
    {
        _stepErrors = StepErrors(_setupStep);
        if (_stepErrors.Count == 0) _setupStep++;
    }

    private void PreviousStep()
    {
        _stepErrors = [];
        _setupStep--;
    }

    /// <summary>The step list moves back freely; forward only past steps that validate.</summary>
    private Task<bool> CanMoveToStepAsync(int index)
    {
        for (var step = _setupStep; step < index + 1; step++)
        {
            _stepErrors = StepErrors(step);
            if (_stepErrors.Count > 0) return Task.FromResult(false);
        }
        _stepErrors = [];
        return Task.FromResult(true);
    }

    private void OnStepSelected(int index) => _setupStep = index + 1;

    private sealed record DnsRecord(string Label, string Value, bool Multiline);
}

public enum MailDialogMode { AddDomain, AddAccount, DnsRecords, AddAlias, RotateDkim, Setup, RequestCertificate, SpamThresholds, LearnSpam, TestDelivery }

public sealed class MailDialogModel : IValidatableObject
{
    private const string DomainPattern = @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$";
    private static readonly RegularExpressionAttribute DomainValidator = new(DomainPattern);
    private static readonly EmailAddressAttribute EmailValidator = new();

    public MailDialogMode Mode { get; set; }
    [StringLength(255)] public string Hostname { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    [StringLength(63)] public string DkimSelector { get; set; } = "default";
    public string Email { get; set; } = string.Empty;
    [StringLength(PasswordPolicy.MaximumLength)] public string Password { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public int DomainId { get; set; }
    public int QuotaMb { get; set; } = 1024;
    public string CurrentSelector { get; set; } = string.Empty;
    [StringLength(63)] public string NewSelector { get; set; } = string.Empty;
    public bool EnableSpamFilter { get; set; } = true;
    public double RejectScore { get; set; } = 15;
    public double AddHeaderScore { get; set; } = 6;
    public double GreylistScore { get; set; } = 4;
    public bool IsSpam { get; set; } = true;
    [StringLength(MailValidation.MaxLearnMessageLength)] public string RawMessage { get; set; } = string.Empty;
    public MailDialogModel Clone() => (MailDialogModel)MemberwiseClone();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ValidateDomain()) yield return result;
        foreach (var result in ValidateAccount()) yield return result;
        foreach (var result in ValidateAlias()) yield return result;
        if (Mode == MailDialogMode.RotateDkim && string.IsNullOrWhiteSpace(NewSelector))
            yield return new ValidationResult("Required", [nameof(NewSelector)]);
        foreach (var result in ValidateStackOperations()) yield return result;
    }

    private IEnumerable<ValidationResult> ValidateDomain()
    {
        if (Mode is MailDialogMode.AddDomain or MailDialogMode.Setup)
        {
            if (string.IsNullOrWhiteSpace(Domain))
                yield return new ValidationResult("Required", [nameof(Domain)]);
            else if (!DomainValidator.IsValid(Domain))
                yield return new ValidationResult("MailDomainInvalid", [nameof(Domain)]);
            // Recette R2-060: the helper refuses a selector it cannot put in a DNS name (mail-setup).
            if (!MailValidation.IsValidDkimSelector(DkimSelector))
                yield return new ValidationResult("MailDkimSelectorInvalid", [nameof(DkimSelector)]);
        }
        // Recette R2-060: mail-setup takes a full name only (a dot and an extension); a bare host name was
        // refused by the agent after the whole wizard.
        if (Mode == MailDialogMode.Setup && !DomainValidator.IsValid(Hostname))
            yield return new ValidationResult("MailHostnameInvalid", [nameof(Hostname)]);
    }

    private IEnumerable<ValidationResult> ValidateAccount()
    {
        if (Mode is MailDialogMode.AddAccount or MailDialogMode.Setup)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
                yield return new ValidationResult("Required", [nameof(Email), nameof(Password)]);
            else
            {
                if (!EmailValidator.IsValid(Email))
                    yield return new ValidationResult("MailEmailInvalid", [nameof(Email)]);
                if (!MailValidation.IsValidPassword(Password))
                    yield return new ValidationResult("MailPasswordInvalid", [nameof(Password)]);
            }
        }
    }

    private IEnumerable<ValidationResult> ValidateStackOperations() => Mode switch
    {
        MailDialogMode.RequestCertificate => EmailValidator.IsValid(Email)
            ? [] : [new ValidationResult("MailEmailInvalid", [nameof(Email)])],
        MailDialogMode.SpamThresholds => MailValidation.AreSpamThresholdsOrdered(GreylistScore, AddHeaderScore, RejectScore)
            ? [] : [new ValidationResult("MailSpamThresholdsInvalid", [nameof(RejectScore)])],
        MailDialogMode.LearnSpam => string.IsNullOrWhiteSpace(RawMessage)
            ? [new ValidationResult("Required", [nameof(RawMessage)])] : [],
        MailDialogMode.TestDelivery => ValidateTestDelivery(),
        MailDialogMode.Setup => ValidateSetupAddress(),
        _ => []
    };

    private IEnumerable<ValidationResult> ValidateTestDelivery()
    {
        if (string.IsNullOrWhiteSpace(Source) || !EmailValidator.IsValid(Source))
            yield return new ValidationResult("MailEmailInvalid", [nameof(Source)]);
        if (string.IsNullOrWhiteSpace(Destination) || !EmailValidator.IsValid(Destination))
            yield return new ValidationResult("MailEmailInvalid", [nameof(Destination)]);
    }

    // The setup helper creates the admin mailbox inside the configured domain.
    private IEnumerable<ValidationResult> ValidateSetupAddress()
    {
        if (!string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Domain)
            && !Email.EndsWith("@" + Domain, StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("MailAdminEmailOutsideDomain", [nameof(Email)]);
    }

    private IEnumerable<ValidationResult> ValidateAlias()
    {
        if (Mode == MailDialogMode.AddAlias)
        {
            if (string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Destination) || DomainId <= 0)
                yield return new ValidationResult("Required", [nameof(Source), nameof(Destination), nameof(DomainId)]);
            else
            {
                if (!EmailValidator.IsValid(Source))
                    yield return new ValidationResult("MailEmailInvalid", [nameof(Source)]);
                if (!EmailValidator.IsValid(Destination))
                    yield return new ValidationResult("MailEmailInvalid", [nameof(Destination)]);
            }
        }
    }
}
