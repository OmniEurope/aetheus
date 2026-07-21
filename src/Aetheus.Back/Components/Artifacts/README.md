# Module Artifacts

## Responsabilité

Stocke, sert et purge les artefacts produits par les pipelines.

## Déploiement et releases

Lorsqu'un artefact est marqué déployé, le service applique sa rétention et, dans la même
transaction relationnelle, rétrograde toute autre release déployée du projet avant de promouvoir
la release explicitement sélectionnée. L'absence ou l'incohérence de cet identifiant de release
fait échouer la clôture du déploiement.

## Points d’entrée

- API ou consommateur principal : `ArtifactsController.cs`.
- Enregistrement DI : `ArtifactsModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
