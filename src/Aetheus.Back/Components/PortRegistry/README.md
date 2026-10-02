# Module PortRegistry

## Responsabilité

Qui détient quel port sur quel serveur, et lequel est réellement en écoute. Répond à « 10041 est-il
libre sur `vps2577917`, et sinon qui l'a ? », question à laquelle rien ne savait répondre avant que
les collisions de ports ne coûtent six runs nocturnes.

Trois sources cohabitent sans se corriger l'une l'autre (`PortReservationSource`) : `Declared` par un
déploiement, `Manual` par un opérateur, `Observed` par un scan de l'agent. Voir
[ADR-045](../../../../docs/adr/ADR-045-port-registry-observed-source.md).

Le protocole fait partie de l'identité d'un port, pas de son étiquette : 53/udp et 53/tcp sont deux
ports différents, d'où `ServerPortReservation.Protocol` dans l'index unique
`(ServerId, Port, Protocol, Source)` (migration `AddServerPortProtocol`, défaut `tcp`). L'agent
observe les deux piles, mais tout ce que le module **défend** reste strictement TCP, puisque c'est ce
qu'un déploiement lie : conflits, vérification et allocation ignorent les lignes UDP. Une ligne UDP
est une observation et rien d'autre, elle ne refuse aucun lancement et ne confirme aucune
revendication. Un protocole illisible est lu comme `tcp`, du côté qui est réellement défendu.

## Points d’entrée

- API : `PortRegistryController.cs` (`api/servers/{serverId}/ports`) - liste, vérification, ajout
  manuel, libération.
- Enregistrement DI : `PortRegistryModuleExtensions.cs`.
- `PortObservationWriter.cs` : l'écriture de ce qu'un scan a **vu**, extraite de `PortRegistryService`.
  Une revendication est une décision que le registre défend, une observation est une preuve qu'il se
  contente d'enregistrer ; les deux responsabilités restent séparées pour qu'aucune ne se mette à
  corriger l'autre. Tout y est clé sur `(port, protocole)`.
- `IPortRegistryService` porte le contrat consommé par les modules au-dessus : la garde de
  préflight des pipelines (`PipelinePortRegistryGuard`) et l'ingestion des observations
  (`Servers`, qui expose le scan à la demande parce que mettre une tâche en file demande `Tasks`,
  hors de portée d'un module L1).

## Limites

Ce module est du stockage (L1) : il ne réclame aucun autre module. Il lit `Servers` et `Projects` par
leurs entités partagées, jamais par leurs services. Une observation n'entraîne jamais de correction
automatique d'une déclaration ; l'écart est rendu visible et tranché par un humain.
