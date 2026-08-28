# Module AgentUpdate

## Responsabilité

Orchestre la disponibilité et la livraison des mises à jour d’agent.

## Points d’entrée

- API : routes de `ServersController` sous `/api/servers/{id}/agent/*` et
  `/api/servers/agent/update-all*`; il n’existe pas de contrôleur dédié.
- Enregistrement DI : `AgentUpdateModuleExtensions.cs`.
- `AgentUpdateService` réserve le serveur, attend son inactivité et crée l’unique
  tâche `AgentSelfUpdate`.
- `AgentCompatibilityPolicy` et `AgentReleaseCatalog` évaluent la fenêtre de
  protocole, les capacités et le manifeste distribué.
- `AgentUpdateRepository` persiste `AgentUpdateRequest`; le coordinateur traite
  la file et `AgentUpdateConfirmationService` confirme uniquement depuis le
  heartbeat d’une nouvelle session.

## Garanties

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.

Le `ServerTask` reste `Running` après le handoff. Le heartbeat conforme d’une
nouvelle session rend atomiquement la demande `Confirmed`, la tâche `Success`
et libère la réservation. Une tâche terminale avant handoff, une mauvaise
version ou l’expiration de confirmation terminent la demande en échec et
libèrent également la réservation.
