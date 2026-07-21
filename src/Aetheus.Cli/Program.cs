// SPDX-License-Identifier: EUPL-1.2
using System.CommandLine;
using System.Net.Http.Json;
using Aetheus.Cli;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;

Option<string> serverOption = new("--server")
{
    Description = "Aetheus API server URL. A Bearer token is refused over plain HTTP to a non-loopback "
        + "host unless the AETHEUS_INSECURE=1 env var is set (escape hatch for trusted private networks).",
    DefaultValueFactory = _ => LocalDevelopmentEndpoints.CliApiBaseUrl,
    Recursive = true
};

Option<string?> tokenOption = new("--token")
{
    Description = "JWT authentication token. WARNING: command-line values may be retained in shell history "
        + "and exposed in process listings; prefer AETHEUS_TOKEN, which overrides this flag.",
    Recursive = true
};

// F-44: read token from AETHEUS_TOKEN env var first so it never appears in shell history
// or process listings; fall back to --token only when the env var is unset.
static string? ResolveToken(string? cliToken)
{
    var envToken = Environment.GetEnvironmentVariable("AETHEUS_TOKEN");
    if (!string.IsNullOrWhiteSpace(envToken)) return envToken;
    if (!string.IsNullOrWhiteSpace(cliToken))
    {
        Console.Error.WriteLine(
            "WARNING: --token can be exposed by shell history or process listings; prefer AETHEUS_TOKEN.");
    }
    return cliToken;
}

Option<int> pageOption = new("--page")
{
    Description = "Page number (1-based)",
    DefaultValueFactory = _ => 1,
    Recursive = true
};

Option<int> pageSizeOption = new("--page-size")
{
    Description = "Items per page (max 200)",
    DefaultValueFactory = _ => 50,
    Recursive = true
};

RootCommand rootCommand = new("Aetheus CLI - manage servers, pipelines, and deployments");
rootCommand.Options.Add(serverOption);
rootCommand.Options.Add(tokenOption);
rootCommand.Options.Add(pageOption);
rootCommand.Options.Add(pageSizeOption);

// Clamp helper: prints a friendly warning when the user-supplied --page or --page-size
// is out of bounds, then returns the safe value used downstream.
static int ClampPage(int requested)
{
    if (requested < 1)
    {
        Console.Error.WriteLine($"Warning: --page must be >= 1 (got {requested}); using 1.");
        return 1;
    }
    return requested;
}

static int ClampPageSize(int requested)
{
    const int min = 1;
    const int max = 200;
    if (requested < min || requested > max)
    {
        var clamped = Math.Clamp(requested, min, max);
        Console.Error.WriteLine($"Warning: --page-size must be between {min} and {max} (got {requested}); using {clamped}.");
        return clamped;
    }
    return requested;
}

// --- server list ---
Command serverListCommand = new("server-list", "List all servers");
rootCommand.Subcommands.Add(serverListCommand);
serverListCommand.SetAction(async parseResult =>
{
    var server = parseResult.GetValue(serverOption)!;
    var token = ResolveToken(parseResult.GetValue(tokenOption));
    var page = ClampPage(parseResult.GetValue(pageOption));
    var pageSize = ClampPageSize(parseResult.GetValue(pageSizeOption));
    using var client = new AetheusApiClient(server, token);
    // The /api/servers endpoint returns a PaginatedResult; using List<ServerDto> would silently
    // deserialize an empty list because the JSON shape differs.
    var result = await client.GetFromJsonAsync<PaginatedResult<ServerDto>>($"api/servers?page={page}&pageSize={pageSize}");
    var servers = result?.Items;
    if (servers is null || servers.Count == 0)
    {
        Console.WriteLine("No servers found.");
        return;
    }
    Console.WriteLine($"{"Id",-5} {"Name",-25} {"Hostname",-30} {"Status",-12}");
    Console.WriteLine(new string('-', 72));
    foreach (var s in servers)
        Console.WriteLine($"{s.Id,-5} {s.Name,-25} {s.Hostname,-30} {s.Status,-12}");
});

// --- pipeline list ---
Command pipelineListCommand = new("pipeline-list", "List all pipelines");
rootCommand.Subcommands.Add(pipelineListCommand);
pipelineListCommand.SetAction(async parseResult =>
{
    var server = parseResult.GetValue(serverOption)!;
    var token = ResolveToken(parseResult.GetValue(tokenOption));
    var page = ClampPage(parseResult.GetValue(pageOption));
    var pageSize = ClampPageSize(parseResult.GetValue(pageSizeOption));
    using var client = new AetheusApiClient(server, token);
    var result = await client.GetFromJsonAsync<PaginatedResult<PipelineDto>>($"api/pipelines?page={page}&pageSize={pageSize}");
    if (result is null || result.Items.Count == 0)
    {
        Console.WriteLine("No pipelines found.");
        return;
    }
    Console.WriteLine($"{"Id",-5} {"Name",-30} {"Project",-20} {"LastRun",-20}");
    Console.WriteLine(new string('-', 75));
    foreach (var p in result.Items)
        Console.WriteLine($"{p.Id,-5} {p.Name,-30} {p.ProjectName,-20} {p.LastRunAt?.ToString("g") ?? "Never",-20}");
});

// --- pipeline run ---
Argument<int> pipelineIdArg = new("pipeline-id")
{
    Description = "ID of the pipeline to run"
};
Command pipelineRunCommand = new("pipeline-run", "Trigger a pipeline run");
pipelineRunCommand.Arguments.Add(pipelineIdArg);
rootCommand.Subcommands.Add(pipelineRunCommand);
pipelineRunCommand.SetAction(async parseResult =>
{
    var pipelineId = parseResult.GetValue(pipelineIdArg);
    var server = parseResult.GetValue(serverOption)!;
    var token = ResolveToken(parseResult.GetValue(tokenOption));
    using var client = new AetheusApiClient(server, token);
    var response = await client.PostAsync($"api/pipelines/{pipelineId}/run", null);
    if (response.IsSuccessStatusCode)
    {
        var run = await response.Content.ReadFromJsonAsync<PipelineRunDto>();
        Console.WriteLine($"Pipeline run started: #{run?.Id} - Status: {run?.Status}");
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        return 1;
    }
});

// --- task list ---
Command taskListCommand = new("task-list", "List recent tasks");
rootCommand.Subcommands.Add(taskListCommand);
taskListCommand.SetAction(async parseResult =>
{
    var server = parseResult.GetValue(serverOption)!;
    var token = ResolveToken(parseResult.GetValue(tokenOption));
    var page = ClampPage(parseResult.GetValue(pageOption));
    var pageSize = ClampPageSize(parseResult.GetValue(pageSizeOption));
    using var client = new AetheusApiClient(server, token);
    var result = await client.GetFromJsonAsync<PaginatedResult<ServerTaskDto>>($"api/tasks?page={page}&pageSize={pageSize}");
    if (result is null || result.Items.Count == 0)
    {
        Console.WriteLine("No tasks found.");
        return;
    }
    Console.WriteLine($"{"Id",-5} {"Name",-35} {"Status",-15} {"Server",-20}");
    Console.WriteLine(new string('-', 75));
    foreach (var t in result.Items)
        Console.WriteLine($"{t.Id,-5} {Truncate(t.Name, 35),-35} {t.Status,-15} {t.ServerName,-20}");
});

// --- health ---
Command healthCommand = new("health", "Check API health");
rootCommand.Subcommands.Add(healthCommand);
healthCommand.SetAction(async parseResult =>
{
    var server = parseResult.GetValue(serverOption)!;
    using var client = new HttpClient { BaseAddress = new Uri(server) };
    var response = await client.GetAsync("health");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Health: {response.StatusCode} - {body}");
    return response.IsSuccessStatusCode ? 0 : 1;
});

return await rootCommand.Parse(args).InvokeAsync();

static string Truncate(string value, int maxLength)
{
    if (value.Length <= maxLength) return value;
    // Defensive: with maxLength <= 3 there is no room for the "..." ellipsis,
    // so fall back to a plain hard cut instead of computing a negative slice index.
    return maxLength <= 3 ? value[..maxLength] : value[..(maxLength - 3)] + "...";
}
