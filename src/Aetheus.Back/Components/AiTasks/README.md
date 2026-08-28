# Module AiTasks

## Responsabilité

Gère les tâches IA (ADR-023) : cycle de vie des runs IA dispatchés aux agents via des profils CLI
(`AiRunnerProfile`), et planification en arrière-plan.

## Points d’entrée

- API ou consommateur principal : `AiTasksController.cs`.
- Enregistrement DI : `AiTasksModuleExtensions.cs`.
- `AiTaskSchedulerService` : service d'arrière-plan qui planifie les runs.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
