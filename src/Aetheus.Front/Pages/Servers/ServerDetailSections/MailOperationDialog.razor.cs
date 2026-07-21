// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class MailOperationDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Parameter] public MailDialogMode Mode { get; set; }
    [Parameter] public MailDialogModel Model { get; set; } = new();
    [Parameter] public IReadOnlyList<MailDomainDto> Domains { get; set; } = [];
    [Parameter] public MailDnsRecordsDto? DnsRecords { get; set; }

    private MailDialogModel _model = new();
    private int _setupStep = 1;
    private string SubmitText => Mode is MailDialogMode.Setup ? L["Install"] : Mode is MailDialogMode.RotateDkim ? L["Rotate"] : L["Add"];
    private IReadOnlyList<DnsRecord> DnsRecordList => DnsRecords is null ? [] :
    [
        new(L["MxRecord"], DnsRecords.MxRecord, false), new("SPF (TXT)", DnsRecords.SpfRecord, false),
        new(L["DkimSelector"], DnsRecords.DkimSelector, false), new($"DMARC (TXT _dmarc.{DnsRecords.Domain})", DnsRecords.DmarcRecord, false),
        new($"DKIM (TXT {DnsRecords.DkimSelector}._domainkey.{DnsRecords.Domain})", DnsRecords.DkimPublicKey, true)
    ];

    protected override void OnParametersSet()
    {
        _model = Model.Clone();
        _model.Mode = Mode;
    }
    private void Submit(MailDialogModel model) => Dialog.Close(model);
    private sealed record DnsRecord(string Label, string Value, bool Multiline);
}

public enum MailDialogMode { AddDomain, AddAccount, DnsRecords, AddAlias, RotateDkim, Setup }

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
    [StringLength(256)] public string Password { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public int DomainId { get; set; }
    public int QuotaMb { get; set; } = 1024;
    public string CurrentSelector { get; set; } = string.Empty;
    [StringLength(63)] public string NewSelector { get; set; } = string.Empty;
    public MailDialogModel Clone() => (MailDialogModel)MemberwiseClone();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Mode is MailDialogMode.AddDomain or MailDialogMode.Setup)
        {
            if (string.IsNullOrWhiteSpace(Domain))
                yield return new ValidationResult("Required", [nameof(Domain)]);
            else if (!DomainValidator.IsValid(Domain))
                yield return new ValidationResult("Invalid domain", [nameof(Domain)]);
        }

        if (Mode is MailDialogMode.AddAccount or MailDialogMode.Setup)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
                yield return new ValidationResult("Required", [nameof(Email), nameof(Password)]);
            else if (!EmailValidator.IsValid(Email))
                yield return new ValidationResult("Invalid email", [nameof(Email)]);
        }

        if (Mode == MailDialogMode.AddAlias)
        {
            if (string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Destination) || DomainId <= 0)
                yield return new ValidationResult("Required", [nameof(Source), nameof(Destination), nameof(DomainId)]);
            else
            {
                if (!EmailValidator.IsValid(Source))
                    yield return new ValidationResult("Invalid source email", [nameof(Source)]);
                if (!EmailValidator.IsValid(Destination))
                    yield return new ValidationResult("Invalid destination email", [nameof(Destination)]);
            }
        }

        if (Mode == MailDialogMode.RotateDkim && string.IsNullOrWhiteSpace(NewSelector))
            yield return new ValidationResult("Required", [nameof(NewSelector)]);
    }
}
