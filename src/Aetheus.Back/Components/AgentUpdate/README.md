# Module AgentUpdate

## Responsabilité

Orchestre la disponibilité et la livraison des mises à jour d’agent.

## Points d’entrée

- API ou consommateur principal : `aucun contrôleur dédié; le service est consommé par les flux d’agent`.
- Enregistrement DI : `AgentUpdateModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
