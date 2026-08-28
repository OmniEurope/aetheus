// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Alerts;

public interface IAlertService
{
    Task<List<AlertRuleDto>> GetAlertRulesAsync(CancellationToken ct = default);
    Task<AlertRuleDto?> GetAlertRuleAsync(int id, CancellationToken ct = default);
    Task<AlertRuleDto> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default);
    Task<AlertRuleDto?> UpdateAlertRuleAsync(int id, UpdateAlertRuleRequest request, CancellationToken ct = default);
    Task<bool> DeleteAlertRuleAsync(int id, CancellationToken ct = default);
}
