# Module Artifacts

## Responsabilité

Stocke, sert et purge les artefacts produits par les pipelines.

## Déploiement et releases

Lorsqu'un artefact est marqué déployé (`MarkDeployedAsync`), le service applique sa rétention et,
quand la clôture du déploiement nomme une release (`AETHEUS_DEPLOY_RELEASE_ID`), rétrograde dans la
même transaction relationnelle toute autre release déployée du projet avant de promouvoir celle-ci.
Un identifiant de release qui n'est pas lié à l'artefact fait échouer la clôture ; sans identifiant,
seule la rétention s'applique.

## Surveillance du volume

`ArtifactStorageMonitorService` mesure le volume physique des artefacts (`ArtifactStorage:BasePath`)
et lève une alerte limitée en fréquence quand le budget (`ArtifactStorage:VolumeBudgetBytes`, 50 Gio
par défaut) est dépassé ou quand la croissance, mesurée sur au moins 24 h, l'atteindrait bientôt
(`ArtifactStorage:GrowthWarningBytesPerDay`, 5 Gio par jour par défaut ; intervalle
`ArtifactStorage:MonitorIntervalMinutes`). Il ne supprime jamais rien. Un seul backend mesure, sous
bail de leader (recette R2-016) ; l'historique des mesures est gardé en base
(`IArtifactStorageMeasurementRepository`, table `ArtifactStorageMeasurements`) et rechargé à chaque
prise de leadership, donc un déploiement ou un changement de leader ne relance plus l'attente de 24 h.

## Points d’entrée

- API ou consommateur principal : `ArtifactsController.cs`.
- Enregistrement DI : `ArtifactsModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
