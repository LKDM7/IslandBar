# IslandBar

Une barre minimale façon Dynamic Island pour Windows 11. Elle reste invisible tant qu'il n'y a rien à montrer, puis apparaît en haut de l'écran pour afficher ta musique et tes notifications Discord.

**[⬇ Télécharger IslandBar-Setup.exe](https://github.com/LKDM7/IslandBar/releases/latest/download/IslandBar-Setup.exe)**

> Application conçue et codée avec **Claude Opus 5.5** d'Anthropic, dans Claude Code.

## Ce qu'elle fait

- **Musique :** titre, pochette, égaliseur animé, progression, lecture/pause, piste précédente et suivante. Elle suit tout lecteur qui publie ses contrôles média à Windows, comme un navigateur.
- **Notifications Discord :** auteur, message et photo de profil s'affichent 5 secondes. Le nombre de notifications non lues reste visible sur la barre.
- **Accès au salon :** cliquer sur une notification ouvre directement son salon ou sa conversation privée.
- **Volume par application :** le réglage agit seulement sur l'application en lecture, pas sur le son général du PC.
- **Discrétion :** sans musique ni notification, la barre est invisible et les clics passent au travers. Elle ne vole jamais le focus, sauf quand tu l'ouvres.

## Raccourcis

`Ctrl + Alt + ²` ouvre ou ferme le panneau. La touche est celle à gauche de `1`, quelle que soit la disposition du clavier.

| Touche | Action |
|---|---|
| `Espace` | Lecture / pause |
| `←` `→` | Piste précédente / suivante |
| `↑` `↓` | Volume de l'application, par pas de 5 % |
| `Entrée` | Ouvrir la notification la plus récente |
| `Suppr` | Effacer les notifications |
| `Échap` | Fermer le panneau |

Quand la barre est visible, un clic l'ouvre aussi. Un clic droit permet de la quitter.

## Installation

Télécharge [`IslandBar-Setup.exe`](https://github.com/LKDM7/IslandBar/releases/latest/download/IslandBar-Setup.exe) depuis les [Releases](https://github.com/LKDM7/IslandBar/releases), puis lance-le. Il faut seulement Windows 11 (x64) et Discord pour ordinateur : .NET est inclus et aucun droit administrateur n'est demandé.

L'installateur n'est pas signé. Si Windows SmartScreen s'affiche, clique sur **Informations complémentaires**, puis sur **Exécuter quand même**.

L'assistant propose de lancer IslandBar au démarrage de Windows et de masquer les bannières Windows de Discord. La désinstallation se fait depuis Paramètres › Applications et efface aussi le jeton Discord.

## Compiler depuis les sources

Il faut Windows 11, Discord pour ordinateur et le [SDK .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/LKDM7/IslandBar.git
cd IslandBar
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o app
.\app\IslandBar.exe
```

Pour vérifier la logique interne (0 = OK) :

```powershell
.\app\IslandBar.exe --selftest; $LASTEXITCODE
```

### Créer l'installateur

Il faut [Inno Setup 6](https://jrsoftware.org/isinfo.php) en plus du SDK.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o dist
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\IslandBar.iss
```

Le résultat est `installer\Output\IslandBar-Setup.exe`.

### Réglages Windows conseillés

L'installateur les propose. Sans lui, l'application ne les pose pas elle-même.

Lancer IslandBar au démarrage de Windows :

```powershell
$exe = (Resolve-Path .\app\IslandBar.exe).Path
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name IslandBar -Value "`"$exe`""
```

Retirer la bannière Windows de Discord, pour éviter une double notification :

```powershell
$discord = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\com.squirrel.Discord.Discord'
Set-ItemProperty $discord -Name ShowBanner -Value 0 -Type DWord
```

Si IslandBar est fermée, ces notifications n'apparaissent alors plus à l'écran. Elles restent dans le centre de notifications.

## Connexion à Discord

Les notifications Windows de Discord ne contiennent pas l'identifiant du salon. Pour y accéder, IslandBar utilise l'API locale de Discord. Au premier lancement, Discord demande une autorisation à accepter une seule fois.

La connexion passe par l'application Discord définie par `ClientId` dans `DiscordRpc.cs`, en mode **Public Client**, avec la redirection `http://127.0.0.1`. Aucun secret n'est nécessaire.

Discord réserve cet accès aux testeurs d'une application non approuvée. Pour un autre compte, il faut soit être ajouté comme testeur, soit créer sa propre application et remplacer `ClientId`.

Sans cette autorisation, tout le reste fonctionne : cliquer sur une notification ouvre simplement Discord.

## Confidentialité

- Rien n'est envoyé ailleurs qu'à `discord.com`, uniquement pour obtenir et renouveler le jeton de connexion.
- Les notifications restent en mémoire et disparaissent à la fermeture de l'application.
- Le jeton est chiffré avec le compte Windows dans `%APPDATA%\IslandBar\discord.token`.
- La base de notifications de Windows est lue uniquement pour récupérer les photos de profil. Seuls les fichiers locaux sont ouverts.

## Limites connues

- Chrome n'a qu'un volume pour tous ses onglets, donc le réglage les touche tous.
- La ligne de volume n'apparaît pas si l'application en lecture n'est pas reconnue, ce qui peut arriver avec Firefox ou certaines applications du Microsoft Store.
- La barre reste invisible par-dessus les jeux en plein écran exclusif. En fenêtré sans bordure, elle s'affiche.
- Il n'est pas possible de répondre à un message depuis la barre.
- Testée uniquement sur un PC sous Windows 11 (build 26200) avec un écran principal.
