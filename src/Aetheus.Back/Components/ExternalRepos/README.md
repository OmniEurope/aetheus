# Module ExternalRepos

## Responsabilité

Synchronise les miroirs de dépôts externes utilisés en lecture et pour les builds.

Un projet a au plus un dépôt externe. Sans dépôt interne, c'est la source du projet. À côté d'un dépôt
interne, c'est une source supplémentaire (recette R-534) : le projet garde sa source, et un pipeline
prend ce miroir pour espace de travail en le nommant dans son YAML (`source: repository:`). HTTPS sans
jeton est une lecture anonyme, pour un dépôt public.

La mécanique du miroir (clone, fetch, identifiants : `ExternalRepoMirrorService`, `CreditedGitRunner`,
`GitCredentialPayload`) vit dans le module Git, que l'orchestrateur peut appeler pour rafraîchir un
miroir juste avant d'épingler le commit d'une exécution (`IExternalMirrorRefresher`). Ce module garde
l'attachement, le détachement et la synchronisation planifiée.

## Points d’entrée

- API ou consommateur principal : `ExternalReposController.cs`.
- Enregistrement DI : `ExternalReposModuleExtensions.cs`.
- Les interfaces `I*` définissent les contrats du module; les services portent la logique et les repositories l’accès persistant lorsqu’il existe.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement aux détails de persistance de ce dossier.
