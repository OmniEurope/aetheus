# Telechargements des agents Aetheus

`Dockerfile.back` publie les projets Linux et Windows, ajoute leurs scripts d'installation, puis
fabrique dans ce dossier deux archives versionnees :

- `aetheus-agent-linux-x64-v${APP_VERSION}.tar.gz` pour Linux ;
- `aetheus-agent-win-x64-v${APP_VERSION}.zip` pour Windows.

`MapAgentDownloads` expose anonymement deux routes publiques stables, independantes du nom physique
versionne de l'archive :

- `/downloads/aetheus-agent-linux-x64.tar.gz` ;
- `/downloads/aetheus-agent-win-x64.zip`.

Avant de servir l'archive Linux, le backend la reconditionne afin d'y injecter
`.aetheus-server-url`, calcule sur l'URL publique configuree de l'API. Les deux routes ajoutent
`X-Content-SHA256`, que l'auto-mise a jour de l'agent verifie en mode fail-closed. En developpement,
`MapAgentDownloads` peut aussi fabriquer les archives a partir des sorties `dotnet publish` locales
si aucune archive preconstruite n'est disponible.

Les autres fichiers places dans ce dossier restent accessibles comme fichiers statiques sous
`/downloads`.
