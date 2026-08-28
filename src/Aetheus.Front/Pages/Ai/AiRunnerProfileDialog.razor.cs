// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Ai;

public partial class AiRunnerProfileDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    [Parameter] public int? ProfileId { get; set; }

    private bool IsEdit => ProfileId.HasValue;
    private EditModel _model = new();
    private bool _busy;
    private bool _loading;

    protected override async Task OnInitializedAsync()
    {
        if (!ProfileId.HasValue) return;
        _loading = true;
        try
        {
            var profile = await Api.Ai.GetAiRunnerProfileAsync(ProfileId.Value);
            if (profile is not null)
            {
                _model = new EditModel
                {
                    Name = profile.Name,
                    Description = profile.Description,
                    Binary = profile.Binary,
                    Arguments = string.Join(Environment.NewLine, profile.ArgsTemplate),
                    Environment = string.Join(Environment.NewLine,
                        profile.Environment.Select(item => $"{item.Key}={item.Value}")),
                    TimeoutSeconds = profile.TimeoutSeconds,
                    MaxOutputBytes = profile.MaxOutputBytes,
                    SendsDataExternally = profile.SendsDataExternally
                };
            }
        }
        catch (HttpRequestException)
        {
            Notify.Error("Error", "LoadFailed");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            var args = SplitLines(_model.Arguments);
            var environment = ParseEnvironment(_model.Environment);
            if (IsEdit)
            {
                var updated = await Api.Ai.UpdateAiRunnerProfileAsync(ProfileId!.Value,
                    new UpdateAiRunnerProfileRequest
                    {
                        Name = _model.Name,
                        Description = _model.Description,
                        Binary = _model.Binary,
                        ArgsTemplate = args,
                        Environment = environment,
                        TimeoutSeconds = _model.TimeoutSeconds,
                        MaxOutputBytes = _model.MaxOutputBytes,
                        SendsDataExternally = _model.SendsDataExternally
                    });
                if (updated is not null)
                {
                    Notify.Success("Updated");
                    Dialog.Close(true);
                }
            }
            else
            {
                var created = await Api.Ai.CreateAiRunnerProfileAsync(new CreateAiRunnerProfileRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    Binary = _model.Binary,
                    ArgsTemplate = args,
                    Environment = environment,
                    TimeoutSeconds = _model.TimeoutSeconds,
                    MaxOutputBytes = _model.MaxOutputBytes,
                    SendsDataExternally = _model.SendsDataExternally
                });
                if (created is not null)
                {
                    Notify.Success("Created");
                    Dialog.Close(true);
                }
            }
        }
        catch (FormatException)
        {
            Notify.Error("ValidationError", "AiEnvironmentFormatError");
        }
        finally
        {
            _busy = false;
        }
    }

    private static List<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static Dictionary<string, string> ParseEnvironment(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(value))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new FormatException("Invalid environment variable format.");
            result[line[..separator].Trim()] = line[(separator + 1)..];
        }
        return result;
    }

    private void Cancel() => Dialog.Close(false);

    private sealed class EditModel
    {
        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required, StringLength(260)]
        public string Binary { get; set; } = string.Empty;

        public string Arguments { get; set; } = "{prompt_file}";
        public string Environment { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 600;
        public int MaxOutputBytes { get; set; } = 200_000;
        public bool SendsDataExternally { get; set; }
    }
}
