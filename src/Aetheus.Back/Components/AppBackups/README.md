# Module AppBackups

## Responsabilité

Gère les politiques, planifications, exécutions et preuves de restore-check des sauvegardes
applicatives rattachées à un projet.

## Points d’entrée

- API : `AppBackupsController.cs` sous `/api/backups` pour la liste paginée, le CRUD des politiques,
  l'historique des runs, le lancement immédiat et les callbacks agent.
- Enregistrement DI : `AppBackupsModuleExtensions.cs`.
- Planification : `BackupSchedulerService.cs` évalue chaque minute les expressions `ScheduleCron` et
  `RestoreCheckCron`; le restore-check cible le dernier run réussi.
- Exécution : l'agent Linux crée un bundle `.aetheus-backup` contenant un dump PostgreSQL/MySQL
  optionnel et les chemins de fichiers configurés. Une politique peut donc sauvegarder une base, des
  fichiers, ou les deux.

## Contrat de vérification

- Le manifeste versionné enregistre la taille et le SHA-256 de chaque entrée; une entrée absente,
  supplémentaire, dupliquée ou altérée fait échouer l'ouverture du bundle.
- Les chemins doivent être absolus, existants, hors racine du système et sans chevauchement. Les liens
  symboliques sont refusés dans les racines et dans les arborescences parcourues.
- Avec PostgreSQL ou MySQL, un restore-check n'est `Verified` qu'après restauration réelle du dump dans
  une base temporaire. Pour une sauvegarde composée uniquement de fichiers, il vérifie l'intégrité et
  l'extraction complète du bundle.
- Les archives restent sur le serveur cible sous le répertoire de sauvegarde de l'agent et sont
  soumises au nombre de rétention de la politique.

## Limites

Les contrôleurs restent minces et délèguent aux services. Les échanges inter-modules passent par les
interfaces publiques et les DTOs de `Aetheus.Shared`; aucun autre module ne doit accéder directement
aux détails de persistance de ce dossier. La gestion d'une politique exige les permissions sur son
projet parent; la création exige en plus `Server.Write` sur le serveur cible. Les callbacks utilisent
le schéma `AgentToken` et sont refusés si le serveur de l'agent ne correspond pas au run.
