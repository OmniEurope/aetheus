// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.Json;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Back.Components.Mail;

public class MailService(IMailRepository repo, IAuditService audit, IEncryptionService encryption, IServerRepository serverRepo, ITaskService taskService) : IMailService
{
    // Persist a queued task AND push the "TaskQueued" SignalR event so the top-bar tracker shows it
    // live (and can later flip it Running/Completed). Mirrors ServerServiceManager (see ITaskService).
    private async Task QueueTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        await repo.AddTaskAsync(task, ct).ConfigureAwait(false);
        await taskService.NotifyTaskQueuedAsync(task, ct: ct).ConfigureAwait(false);
    }

    public async Task<MailDataDto> GetStateAsync(int serverId, CancellationToken ct = default)
    {
        var state = await repo.GetStateAsync(serverId, ct).ConfigureAwait(false);
        if (state is null)
            return new MailDataDto();

        return new MailDataDto
        {
            IsInstalled = true,
            IsPostfixRunning = state.IsPostfixRunning,
            IsDovecotRunning = state.IsDovecotRunning,
            PostfixVersion = state.PostfixVersion,
            DovecotVersion = state.DovecotVersion,
            QueueSize = state.QueueSize
        };
    }

    public async Task<List<MailDomainDto>> GetDomainsAsync(int serverId, CancellationToken ct = default)
    {
        var domains = await repo.GetDomainsAsync(serverId, ct).ConfigureAwait(false);
        return domains.Select(MapDomain).ToList();
    }

    public async Task<PaginatedResult<MailDomainDto>> GetDomainsAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, total) = await repo.GetDomainsPagedAsync(
            serverId, request.Search, page, pageSize, request.SortBy, request.SortDescending, ct).ConfigureAwait(false);
        return Page(items.Select(MapDomain), total, page, pageSize);
    }

    public async Task<MailDomainDto> GetDomainAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");
        return MapDomain(domain);
    }

    public async Task<MailDomainDto> CreateDomainAsync(int serverId, CreateMailDomainRequest request, CancellationToken ct = default)
    {
        if (!MailCommandHelper.IsValidDomainName(request.Name))
            throw new BadRequestException("Invalid domain name.");

        var domain = new MailDomain
        {
            ServerId = serverId,
            Name = request.Name,
            DkimSelector = request.DkimSelector
        };

        await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);
        await repo.AddDomainAsync(domain, ct).ConfigureAwait(false);

        // S-FEAT-W8KN: dispatch the typed MailAddDomain operation via the root-owned mail-manage helper
        // (the old free-form postconf/systemctl shell task always failed for the non-root agent). The
        // new domain is the operation target; no secret is involved. Queued via QueueTaskAsync so the
        // TaskQueued SignalR event is broadcast (top-bar tracker).
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - add domain {request.Name}",
            OperationKind.MailAddDomain, request.Name, timeoutSeconds: 30), ct).ConfigureAwait(false);

        await audit.LogAsync("MailAddDomain", "Mail", serverId, request.Name, ct).ConfigureAwait(false);
        return MapDomain(domain);
    }

    public async Task<MailDomainDto> UpdateDomainAsync(int serverId, int domainId, UpdateMailDomainRequest request, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");

        if (request.IsActive.HasValue)
            domain.IsActive = request.IsActive.Value;
        if (request.DkimSelector is not null)
        {
            if (!MailCommandHelper.IsValidDkimSelector(request.DkimSelector))
                throw new BadRequestException("Invalid DKIM selector.");
            domain.DkimSelector = request.DkimSelector;
        }

        await repo.UpdateDomainAsync(domain, ct).ConfigureAwait(false);
        await audit.LogAsync("MailUpdateDomain", "Mail", serverId, domain.Name, ct).ConfigureAwait(false);
        return MapDomain(domain);
    }

    public async Task DeleteDomainAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");

        await repo.DeleteDomainAsync(domain, ct).ConfigureAwait(false);

        // Typed op via the root-owned mail-manage helper (replaces the dead `postconf $(...) &&` shell).
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - remove domain {domain.Name}",
            OperationKind.MailRemoveDomain, target: domain.Name, timeoutSeconds: 30), ct).ConfigureAwait(false);

        await audit.LogAsync("MailRemoveDomain", "Mail", serverId, domain.Name, ct).ConfigureAwait(false);
    }

    public async Task<List<MailAccountDto>> GetAccountsAsync(int serverId, CancellationToken ct = default)
    {
        var accounts = await repo.GetAccountsAsync(serverId, ct).ConfigureAwait(false);
        return accounts.Select(MapAccount).ToList();
    }

    public async Task<PaginatedResult<MailAccountDto>> GetAccountsAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, total) = await repo.GetAccountsPagedAsync(
            serverId, request.Search, page, pageSize, request.SortBy, request.SortDescending, ct).ConfigureAwait(false);
        return Page(items.Select(MapAccount), total, page, pageSize);
    }

    public async Task<MailAccountDto> CreateAccountAsync(int serverId, CreateMailAccountRequest request, CancellationToken ct = default)
    {
        if (!MailCommandHelper.IsValidEmail(request.Email))
            throw new BadRequestException("Invalid email address.");
        if (!MailCommandHelper.IsValidPassword(request.Password))
            throw new BadRequestException("Password must be 8–128 characters and contain no control characters.");

        // Find the domain for this email
        var emailDomain = request.Email.Split('@')[1];
        var domains = await repo.GetDomainsAsync(serverId, ct).ConfigureAwait(false);
        var domain = domains.FirstOrDefault(d => d.Name.Equals(emailDomain, StringComparison.OrdinalIgnoreCase))
            ?? throw new BadRequestException($"Domain '{emailDomain}' is not configured on this server.");

        await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);

        var account = new MailAccount
        {
            MailDomainId = domain.Id,
            Email = request.Email,
            QuotaMb = request.QuotaMb,
            IsActive = true
        };

        await repo.AddAccountAsync(account, ct).ConfigureAwait(false);

        // S-FEAT-W8KN: typed MailAddAccount via the mail-manage helper. The email is the target; the
        // parent domain and quota ride in env vars and the password is piped to the helper over stdin
        // (never argv). The password-bearing env JSON is encrypted at rest like MailSetup.
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MailSetupEnv.Domain] = emailDomain,
            [MailSetupEnv.QuotaMb] = request.QuotaMb.ToString(CultureInfo.InvariantCulture),
            [MailSetupEnv.AccountPassword] = request.Password
        };
        var task = ServerTaskFactory.Operation(serverId, $"Mail - add account {request.Email}",
            OperationKind.MailAddAccount, request.Email, env, timeoutSeconds: 30);
        task.EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(env));
        await QueueTaskAsync(task, ct).ConfigureAwait(false);

        await audit.LogAsync("MailAddAccount", "Mail", serverId, request.Email, ct).ConfigureAwait(false);
        return MapAccount(account);
    }

    public async Task<MailAccountDto> UpdateAccountAsync(int serverId, int accountId, UpdateMailAccountRequest request, CancellationToken ct = default)
    {
        var account = await repo.GetAccountAsync(accountId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail account {accountId} not found.");

        if (account.MailDomain.ServerId != serverId)
            throw new NotFoundException($"Mail account {accountId} not found on this server.");

        if (request.QuotaMb.HasValue)
            account.QuotaMb = request.QuotaMb.Value;
        if (request.IsActive.HasValue)
            account.IsActive = request.IsActive.Value;
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (!MailCommandHelper.IsValidPassword(request.NewPassword))
                throw new BadRequestException("Password must be 8-128 characters and contain no control characters.");

            await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);

            // S-TECH-MCPW: typed MailChangePassword via the mail-manage helper. The email is the target;
            // the new password rides in an encrypted env var and is piped to the helper over stdin (never
            // argv), mirroring MailAddAccount. Replaces the old free-form `doveadm pw -p` shell task that
            // put the cleartext password on the process argv.
            var pwdEnv = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MailSetupEnv.AccountPassword] = request.NewPassword
            };
            var pwdTask = ServerTaskFactory.Operation(serverId, $"Mail - change password {account.Email}",
                OperationKind.MailChangePassword, account.Email, pwdEnv, timeoutSeconds: 15);
            pwdTask.EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(pwdEnv));
            await QueueTaskAsync(pwdTask, ct).ConfigureAwait(false);
        }

        await repo.UpdateAccountAsync(account, ct).ConfigureAwait(false);
        await audit.LogAsync("MailUpdateAccount", "Mail", serverId, account.Email, ct).ConfigureAwait(false);
        return MapAccount(account);
    }

    public async Task DeleteAccountAsync(int serverId, int accountId, CancellationToken ct = default)
    {
        var account = await repo.GetAccountAsync(accountId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail account {accountId} not found.");

        if (account.MailDomain.ServerId != serverId)
            throw new NotFoundException($"Mail account {accountId} not found on this server.");

        var domain = account.MailDomain.Name;
        var email = account.Email;

        await repo.DeleteAccountAsync(account, ct).ConfigureAwait(false);

        // Typed op via mail-manage (replaces the dead `sed -i ... && postmap ...` shell). The parent
        // domain travels in env so the helper can re-validate the account belongs to it.
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - delete account {email}",
            OperationKind.MailDeleteAccount, target: email,
            environmentVariables: new Dictionary<string, string> { [MailSetupEnv.Domain] = domain },
            timeoutSeconds: 15), ct).ConfigureAwait(false);

        await audit.LogAsync("MailDeleteAccount", "Mail", serverId, email, ct).ConfigureAwait(false);
    }

    public async Task ExecuteActionAsync(int serverId, MailActionRequest request, CancellationToken ct = default)
    {
        var op = request.Action switch
        {
            MailAction.StartPostfix => (OperationKind?)OperationKind.MailStartPostfix,
            MailAction.StopPostfix => OperationKind.MailStopPostfix,
            MailAction.RestartPostfix => OperationKind.MailRestartPostfix,
            MailAction.ReloadPostfix => OperationKind.MailReloadPostfix,
            MailAction.StartDovecot => OperationKind.MailStartDovecot,
            MailAction.StopDovecot => OperationKind.MailStopDovecot,
            MailAction.RestartDovecot => OperationKind.MailRestartDovecot,
            MailAction.ReloadDovecot => OperationKind.MailReloadDovecot,
            MailAction.FlushQueue => OperationKind.MailFlushQueue,
            MailAction.ViewQueue => OperationKind.MailViewQueue,
            MailAction.TestConfig => OperationKind.MailTestConfig,
            _ => null
        };

        if (op is { } kind)
        {
            await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - {request.Action}", kind, target: "-", timeoutSeconds: 30), ct).ConfigureAwait(false);
        }
        else
        {
            // Fallback to shell for complex operations (SpamAssassin service control)
            var command = MailCommandHelper.BuildServiceCommand(request.Action);
            await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Mail - {request.Action}", command, 30), ct).ConfigureAwait(false);
        }

        await audit.LogAsync($"Mail{request.Action}", "Mail", serverId, request.Action.ToString(), ct).ConfigureAwait(false);
    }

    public async Task GetLogsAsync(int serverId, MailLogRequest request, CancellationToken ct = default)
    {
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail logs - {request.LogType}", OperationKind.MailGetLogs, target: request.LogType, timeoutSeconds: 15), ct).ConfigureAwait(false);
    }

    public async Task SetupAsync(int serverId, MailSetupRequest request, CancellationToken ct = default)
    {
        if (!MailCommandHelper.IsValidDomainName(request.Hostname))
            throw new BadRequestException("Invalid hostname.");
        if (!MailCommandHelper.IsValidDomainName(request.Domain))
            throw new BadRequestException("Invalid domain.");
        if (!MailCommandHelper.IsValidEmail(request.AdminEmail))
            throw new BadRequestException("Invalid admin email.");
        if (!MailCommandHelper.IsValidPassword(request.AdminPassword))
            throw new BadRequestException("Admin password must be 8–128 characters and contain no control characters.");
        if (!MailCommandHelper.IsValidDkimSelector(request.DkimSelector))
            throw new BadRequestException("Invalid DKIM selector.");
        if (!MailValidation.IsValidQuotaMb(request.QuotaMb))
            throw new BadRequestException("Invalid mailbox quota.");

        // F6: gate on the server's reported mail-setup capability so the UI never queues an action the
        // agent cannot perform - without the aetheus-mail sudoers grant the helper's `sudo -n` would
        // refuse in silence. Mirrors the package-manage gate in ServerServiceManager.
        var server = await serverRepo.FindServerAsync(serverId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Server {serverId} not found.");
        if (!server.MailSetupAvailable)
            throw new BadRequestException(
                "Mail setup is not enabled on this server. Re-run the agent installer with the mail-setup " +
                "capability (--enable-mail-setup) to grant the controlled-sudo helper.");

        // S-FEAT-W8KN: dispatch the typed MailSetup operation instead of a free-form shell pipeline.
        // The non-root agent could never run the old apt/postconf/systemctl pipeline directly; it now
        // invokes the root-owned `mail-setup` helper through the argv-exact aetheus-mail sudoers
        // grant. The domain is the operation target; hostname/selector/email/quota travel as env vars,
        // and the admin password is piped to the helper over stdin (never argv - off the process list).
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MailSetupEnv.Hostname] = request.Hostname,
            [MailSetupEnv.DkimSelector] = request.DkimSelector,
            [MailSetupEnv.AdminEmail] = request.AdminEmail,
            [MailSetupEnv.QuotaMb] = request.QuotaMb.ToString(CultureInfo.InvariantCulture),
            [MailSetupEnv.AdminPassword] = request.AdminPassword
        };

        var task = ServerTaskFactory.Operation(serverId, $"Mail - full setup ({request.Domain})",
            OperationKind.MailSetup, request.Domain, env, timeoutSeconds: 300);
        // F-001 / F11b: the admin password rides in EnvironmentVariables. Encrypt it at rest (AES-256)
        // like every other secret-bearing task instead of leaving plaintext JSON in the DB - the
        // backend decrypts it only when the task is dispatched to the agent (TaskService env mapping).
        task.EnvironmentVariables = TaskEnvProtection.Protect(encryption, JsonSerializer.Serialize(env));
        await QueueTaskAsync(task, ct).ConfigureAwait(false);

        // Create domain and account records
        var domain = new MailDomain
        {
            ServerId = serverId,
            Name = request.Domain,
            DkimSelector = request.DkimSelector
        };
        await repo.AddDomainAsync(domain, ct).ConfigureAwait(false);

        var account = new MailAccount
        {
            MailDomainId = domain.Id,
            Email = request.AdminEmail,
            QuotaMb = request.QuotaMb,
            IsActive = true
        };
        await repo.AddAccountAsync(account, ct).ConfigureAwait(false);

        await audit.LogAsync("MailSetup", "Mail", serverId, request.Domain, ct).ConfigureAwait(false);
    }

    public async Task<MailDnsRecordsDto> GetDnsRecordsAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");

        // Read DKIM key from server via typed op (replaces the dead `cat ... 2>/dev/null` shell). The
        // helper cats /etc/opendkim/keys/{selector}.txt as root.
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - read DKIM key ({domain.Name})",
            OperationKind.MailDkimRead, target: domain.DkimSelector, timeoutSeconds: 10), ct).ConfigureAwait(false);

        return new MailDnsRecordsDto
        {
            Domain = domain.Name,
            MxRecord = $"10 mail.{domain.Name}.",
            SpfRecord = $"v=spf1 mx a ~all",
            DkimSelector = domain.DkimSelector,
            DkimRecord = $"{domain.DkimSelector}._domainkey.{domain.Name}",
            DmarcRecord = $"v=DMARC1; p=quarantine; rua=mailto:postmaster@{domain.Name}"
        };
    }

    private static MailDomainDto MapDomain(MailDomain domain) => new()
    {
        Id = domain.Id,
        Name = domain.Name,
        IsActive = domain.IsActive,
        DkimSelector = domain.DkimSelector,
        HasSpf = domain.HasSpf,
        HasDkim = domain.HasDkim,
        HasDmarc = domain.HasDmarc,
        CreatedAt = domain.CreatedAt
    };

    private static PaginatedResult<T> Page<T>(IEnumerable<T> items, int total, int page, int pageSize) => new()
    {
        Items = items.ToList(),
        TotalCount = total,
        Page = page,
        PageSize = pageSize
    };

    private static MailAccountDto MapAccount(MailAccount account) => new()
    {
        Id = account.Id,
        Email = account.Email,
        QuotaMb = account.QuotaMb,
        IsActive = account.IsActive,
        CreatedAt = account.CreatedAt
    };

    private static MailAliasDto MapAlias(MailAlias alias) => new()
    {
        Id = alias.Id,
        SourceEmail = alias.SourceEmail,
        DestinationEmail = alias.DestinationEmail,
        IsActive = alias.IsActive,
        CreatedAt = alias.CreatedAt
    };

    private static ServerTask BuildAddAliasTask(int serverId, string source, string destination)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MailSetupEnv.AliasDestination] = destination
        };
        return ServerTaskFactory.Operation(serverId, $"Mail - alias {source} → {destination}",
            OperationKind.MailAddAlias, source, env, timeoutSeconds: 15);
    }

    // S-FEAT-W8KN: every incremental mail op runs through the root-owned mail-manage helper, which the
    // installer only deposits with the mail-setup capability (the aetheus-mail sudoers grant). Gate
    // here so the UI never queues an op the non-root agent's `sudo -n` would silently refuse - mirrors
    // SetupAsync's MailSetupAvailable check.
    private async Task EnsureMailManageableAsync(int serverId, CancellationToken ct)
    {
        var server = await serverRepo.FindServerAsync(serverId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Server {serverId} not found.");
        if (!server.MailSetupAvailable)
            throw new BadRequestException(
                "Mail management is not enabled on this server. Re-run the agent installer with the mail-setup " +
                "capability (--enable-mail-setup) to grant the controlled-sudo helper.");
    }

    // --- Aliases ---

    public async Task<List<MailAliasDto>> GetAliasesAsync(int serverId, CancellationToken ct = default)
    {
        var aliases = await repo.GetAliasesAsync(serverId, ct).ConfigureAwait(false);
        return aliases.Select(MapAlias).ToList();
    }

    public async Task<PaginatedResult<MailAliasDto>> GetAliasesAsync(
        int serverId, PaginationRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, total) = await repo.GetAliasesPagedAsync(
            serverId, request.Search, page, pageSize, request.SortBy, request.SortDescending, ct).ConfigureAwait(false);
        return Page(items.Select(MapAlias), total, page, pageSize);
    }

    public async Task<MailAliasDto> CreateAliasAsync(int serverId, int domainId, CreateMailAliasRequest request, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");

        if (!MailCommandHelper.IsValidEmail(request.SourceEmail))
            throw new BadRequestException("Invalid source email address.");

        if (!MailCommandHelper.IsValidEmail(request.DestinationEmail))
            throw new BadRequestException("Invalid destination email address.");

        await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);

        var alias = new MailAlias
        {
            MailDomainId = domain.Id,
            SourceEmail = request.SourceEmail,
            DestinationEmail = request.DestinationEmail
        };
        await repo.AddAliasAsync(alias, ct).ConfigureAwait(false);

        // S-FEAT-W8KN: typed MailAddAlias via the mail-manage helper. Source email is the target; the
        // destination rides in an env var. No secret involved. Queued via QueueTaskAsync (TaskQueued broadcast).
        await QueueTaskAsync(BuildAddAliasTask(serverId, request.SourceEmail, request.DestinationEmail), ct).ConfigureAwait(false);

        await audit.LogAsync("MailAliasCreated", "Mail", serverId, request.SourceEmail, ct).ConfigureAwait(false);
        return MapAlias(alias);
    }

    public async Task<MailAliasDto> UpdateAliasAsync(int serverId, int aliasId, UpdateMailAliasRequest request, CancellationToken ct = default)
    {
        var alias = await repo.GetAliasAsync(aliasId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail alias {aliasId} not found.");

        if (alias.MailDomain.ServerId != serverId)
            throw new NotFoundException($"Mail alias {aliasId} not found.");

        if (request.IsActive.HasValue)
            alias.IsActive = request.IsActive.Value;
        if (request.DestinationEmail is not null)
        {
            if (!MailCommandHelper.IsValidEmail(request.DestinationEmail))
                throw new BadRequestException("Invalid destination email address.");
            alias.DestinationEmail = request.DestinationEmail;
        }

        await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);
        await repo.UpdateAliasAsync(alias, ct).ConfigureAwait(false);

        // Rebuild the alias on the server (add-alias is idempotent: the helper rewrites the line).
        await QueueTaskAsync(BuildAddAliasTask(serverId, alias.SourceEmail, alias.DestinationEmail), ct).ConfigureAwait(false);

        await audit.LogAsync("MailAliasUpdated", "Mail", serverId, alias.SourceEmail, ct).ConfigureAwait(false);
        return MapAlias(alias);
    }

    public async Task DeleteAliasAsync(int serverId, int aliasId, CancellationToken ct = default)
    {
        var alias = await repo.GetAliasAsync(aliasId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail alias {aliasId} not found.");

        if (alias.MailDomain.ServerId != serverId)
            throw new NotFoundException($"Mail alias {aliasId} not found.");

        await repo.DeleteAliasAsync(alias, ct).ConfigureAwait(false);

        // Typed op via mail-manage (replaces the dead `sed -i ... && postmap ...` shell).
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId, $"Mail - remove alias {alias.SourceEmail}",
            OperationKind.MailRemoveAlias, target: alias.SourceEmail, timeoutSeconds: 15), ct).ConfigureAwait(false);

        await audit.LogAsync("MailAliasDeleted", "Mail", serverId, alias.SourceEmail, ct).ConfigureAwait(false);
    }

    // --- DKIM key rotation ---

    public async Task<DkimRotationResultDto> RotateDkimKeyAsync(int serverId, int domainId, DkimRotationRequest request, CancellationToken ct = default)
    {
        var domain = await repo.GetDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");

        if (!MailCommandHelper.IsValidDkimSelector(request.NewSelector))
            throw new BadRequestException("Invalid DKIM selector.");

        await EnsureMailManageableAsync(serverId, ct).ConfigureAwait(false);

        var oldSelector = domain.DkimSelector;
        domain.DkimSelector = request.NewSelector;
        await repo.UpdateDomainAsync(domain, ct).ConfigureAwait(false);

        // S-FEAT-W8KN: typed MailDkimRotate via the mail-manage helper. The domain is the target; the new
        // selector rides in an env var. The helper regenerates the key, rewrites KeyTable/SigningTable,
        // restarts opendkim and emits the public-key TXT record on stdout. Queued via QueueTaskAsync (broadcast).
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MailSetupEnv.NewSelector] = request.NewSelector
        };
        await QueueTaskAsync(ServerTaskFactory.Operation(serverId,
            $"Mail - rotate DKIM key for {domain.Name} ({oldSelector} → {request.NewSelector})",
            OperationKind.MailDkimRotate, domain.Name, env, timeoutSeconds: 30), ct).ConfigureAwait(false);

        await audit.LogAsync("DkimKeyRotated", "Mail", serverId, $"{domain.Name}: {oldSelector} → {request.NewSelector}", ct).ConfigureAwait(false);

        return new DkimRotationResultDto
        {
            Domain = domain.Name,
            OldSelector = oldSelector,
            NewSelector = request.NewSelector,
            DnsRecordName = $"{request.NewSelector}._domainkey.{domain.Name}",
            DnsRecordValue = "(key will be generated by server task)"
        };
    }
}
