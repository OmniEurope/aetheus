// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.AppBackups;

/// <summary>Domain-specific operations carried by the scoped EntityHub feed.</summary>
public static class OperationalRealtimeEvents
{
    public const string BackupChanged = "BackupChanged";

    /// <summary>A monitored application of the project (the event id) stored new OTLP logs or errors.</summary>
    public const string AppTelemetryChanged = "AppTelemetryChanged";
}
