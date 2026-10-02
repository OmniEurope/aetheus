# Module PortAllocation

## Responsabilité

Allouer des ports libres directement dans une bibliothèque de variables : réserver dans le registre,
puis écrire les entrées `PORT_*`. Les deux tiennent dans une transaction unique sur un fournisseur
relationnel : `DbTransactionScope` est réentrant, donc le service des bibliothèques rejoint la
transaction ouverte au lieu d'en ouvrir une seconde sur le même contexte, ce qu'EF refuse. Si une
entrée échoue, les ports réservés sont aussi libérés en compensation, ceinture du fournisseur
InMemory qui n'a pas de transactions. Ce module existe parce que `PortRegistry`
et `VariableLibraries` sont tous deux du stockage (L1) et ne peuvent donc pas s'appeler l'un l'autre.

Deux règles portent la valeur du module et ne doivent pas bouger :

- la réservation est écrite sous **la clé du projet** de la bibliothèque ; sous une clé manuelle, elle
  bloquerait le déploiement même pour lequel elle a été faite ;
- une clé sans le préfixe `PORT_` est **refusée**, jamais renommée : la garde de préflight des
  pipelines ne lit que ce préfixe, donc une autre clé produirait une variable qui semble protégée et
  ne l'est pas.

Une bibliothèque sans projet (environnement partagé) est refusée avec la raison, plutôt que réservée
sous une identité qu'aucun déploiement ne reconnaîtrait.

## Points d’entrée

- API : `PortAllocationController.cs` (`api/variable-libraries/{libraryId}/ports`) - `servers`
  (candidats + raison de blocage), `check` (entrées `PORT_*` existantes), `allocate`.
- Enregistrement DI : `PortAllocationModuleExtensions.cs`.
- `IPortAllocationRepository` résout les serveurs candidats d'une portée (projet, environnement,
  serveur-projet) directement sur les entités partagées : aucun service existant ne rend cette
  jointure, et la faire passer par les modules propriétaires ajouterait des dépendances pour une
  lecture que ce module est seul à demander.

## Limites

L2 : ce module lit `PortRegistry` et `VariableLibraries` par leurs interfaces publiques, et rien
au-dessus ne le connaît. Droits (décision D3 de PLAN-005) : allouer demande **Write bibliothèque +
Read serveur**, jamais Write serveur, la réservation étant écrite par le système sous l'identité du
projet comme le fait déjà un lancement de pipeline.
