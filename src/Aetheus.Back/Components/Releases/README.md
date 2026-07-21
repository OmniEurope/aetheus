# Module Releases

## Responsabilité

Gère les releases, leur publication et les événements de rollback.

## Invariants de déploiement

Une seule release `Deployed` peut exister par projet. La publication, la promotion et la
rétrogradation associées à un déploiement sont transactionnelles ; une migration PostgreSQL
normalise les doublons historiques puis impose cet invariant par un index unique filtré. Le
déploiement transmet l'identifiant exact de la release sélectionnée jusqu'à sa clôture : une
release non liée à l'artefact déployé est refusée.

## Points d’entrée

- API ou consommateur principal : `ReleasesController.cs`.
- Enregistrement DI : `ReleasesModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
