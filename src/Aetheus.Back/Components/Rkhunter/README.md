# Module Rkhunter

## Responsabilité

Expose les scans Rkhunter et leur planification.

## Points d’entrée

- API ou consommateur principal : `RkhunterController.cs`.
- Enregistrement DI : `RkhunterModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
