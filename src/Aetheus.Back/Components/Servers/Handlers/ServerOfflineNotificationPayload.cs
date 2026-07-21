// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Servers.Handlers;

public record ServerOfflineNotificationPayload(int ServerId, string ServerName, DateTime? LastHeartbeat);
