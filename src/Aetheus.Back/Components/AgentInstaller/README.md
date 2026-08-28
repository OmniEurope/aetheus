# Module AgentInstaller

## Responsabilité

Construit et expose un script d'installation one-shot Bash ou PowerShell, prérempli avec l'URL
publique, la version et un token d'enrôlement valide. Les archives binaires stables restent servies
séparément depuis `wwwroot/downloads` par les routes `/downloads/aetheus-agent-linux-x64.tar.gz`,
`/downloads/aetheus-agent-win-x64.zip` et `/downloads/releases/{version}/{fileName}`.

## Points d’entrée

- `GET /api/agent/installer/{platform}` (Admin) retourne le script pour la plateforme demandée.
- Le token d'enrôlement doit de préférence être transmis via `X-Registration-Token` ou
  `Authorization: Bearer`. Le query parameter historique reste accepté comme fallback de compatibilité
  dans tous les environnements ; il est déconseillé, car les URL peuvent être enregistrées par les proxies.
- Les options `version` et `pipelineRunner` paramètrent les deux plateformes ;
  `serverManagement` s'applique uniquement au script Linux.
- API ou consommateur principal : `AgentInstallerController.cs`.
- Enregistrement DI : `AgentInstallerModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
