# Module AppMonitoring

## Responsabilité

Collecte la télémétrie applicative, calcule l’état de santé et applique la rétention.

## Points d’entrée

- API ou consommateur principal : `AppMonitoringController.cs`, `AppTelemetryController.cs`,
  `Ingest/IngestController.cs` et `PublicWebAnalyticsController.cs` (ingestion publique anonyme
  `api/ingest/web-analytics/v1/public/{siteId}`).
- Enregistrement DI : `AppMonitoringModuleExtensions.cs`.
- Clés d'ingestion : `IngestKeyRotation` garde deux clés précédentes lors d'une rotation de
  déploiement (une par couleur blue-green, recette R2-013), une seule lors d'une rotation demandée par
  un opérateur, dans la limite de `AppMonitoring:IngestKeyOverlapDays` ; `Ingest/IngestKeyRejectionLog`
  déduplique les refus par clé et par heure.
- Budget d'audience : à son budget, une application roule ses plus anciennes lignes jusqu'à 90 %
  (`AppWebAnalyticsService.RollTargetPercent`) ; au-delà de `AppMonitoring:Analytics:RollingEventsPerHour`
  événements acceptés sur l'heure glissante (5 000 par défaut, borné entre 100 et 1 000 000, compté dans
  `AppAnalyticsIngestVolumes` pour les deux couleurs), le lot est refusé avec le motif `flood`. Voir
  [le runbook](../../../../docs/runbooks/telemetry-and-web-analytics.md).
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
