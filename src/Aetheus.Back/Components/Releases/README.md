# Module Releases

## Responsabilité

Gère les releases, leur publication et les événements de rollback.

## Invariants de déploiement

Une seule release `Deployed` peut exister par projet. La publication, la promotion et la
rétrogradation associées à un déploiement sont transactionnelles ; une migration PostgreSQL
normalise les doublons historiques puis impose cet invariant par un index unique filtré. Le
déploiement transmet l'identifiant exact de la release sélectionnée jusqu'à sa clôture : une
release non liée à l'artefact déployé est refusée.

Une release passe en `Deployed` de deux façons, et les deux lèvent `ReleaseDeployedEvent` : la
clôture d'une étape `type: deploy`, et une étape `type: release` avec `deployed: true` (recette
R-520), annoncée par `ReleaseDeploymentAnnouncer` avec le stage que l'agent appelant exécute dans ce
run. L'avance de `main` n'en dépend plus : c'est l'étape `type: advance-branch` du module
Pipelines (recette R2-001). Une étape qui enregistre un état de
déploiement ne change pas le commit d'une release existante : il reste celui de sa candidate
(`ReleaseRunOutcome.KeepsRecordedCommit`).

## Points d’entrée

- API ou consommateur principal : `ReleasesController.cs` (y compris les releases d'un serveur,
  `GET /api/servers/{serverId}/releases` et son `filter-values`) et `ReleaseProvenanceController.cs`
  (`GET /api/releases/{id}/provenance`).
- Enregistrement DI : `ReleasesModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
