# Module Projects

## Responsabilité

Gère les projets et leurs relations avec dépôts, serveurs et pipelines.

## Points d’entrée

- API ou consommateur principal : `ProjectsController.cs`.
- Enregistrement DI : `ProjectsModuleExtensions.cs`.
- Le créateur d'un projet le suit dès sa création (`ProjectService.CreateProjectAsync`, recette
  R2-034), pour que les notifications du projet aient au moins un destinataire.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
