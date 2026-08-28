# Module AppMonitoring

## Responsabilité

Collecte la télémétrie applicative, calcule l’état de santé et applique la rétention.

## Points d’entrée

- API ou consommateur principal : `AppMonitoringController.cs`, `AppTelemetryController.cs`,
  `Ingest/IngestController.cs` et `PublicWebAnalyticsController.cs` (ingestion publique anonyme
  `api/ingest/web-analytics/v1/public/{siteId}`).
- Enregistrement DI : `AppMonitoringModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
