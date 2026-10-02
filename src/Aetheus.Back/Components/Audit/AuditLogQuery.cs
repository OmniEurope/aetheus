// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Audit;

/// <summary>
/// Recette R-238: the column filters of the audit grids (the audit log page and an entity's audit
/// trail). The timestamp range replaces the two date pickers that sat above the grid.
/// </summary>
internal static class AuditLogQuery
{
    internal static readonly GridQueryMap<AuditLog> Columns = new GridQueryMap<AuditLog>()
        .Date("timestamp", a => a.Timestamp)
        .Text("username", a => a.Username)
        .Text("action", a => a.Action)
        .Text("entityType", a => a.EntityType)
        .Number("entityId", a => a.EntityId)
        .Text("details", a => a.Details);
}
