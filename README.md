<img src="assets/logo/logo-256.png" alt="Logo d'IslandBar Lite" width="96">

# IslandBar Lite

[English](README.en.md) | **Français**

Une barre minimale façon Dynamic Island pour Windows 11. Elle reste invisible tant qu'il n'y a rien à montrer, puis apparaît en haut de l'écran pour afficher ta musique, tes notifications et les applications qui utilisent ton micro ou ta caméra.

**[⬇ Télécharger IslandBar-Lite-Setup.exe](https://github.com/LKDM7/IslandBar-Lite/releases/latest/download/IslandBar-Lite-Setup.exe)**

<img src="assets/demo/demo-fr.gif" alt="Démonstration : la barre affiche une musique, une notification Discord, le panneau complet, les réglages puis le point du micro" width="420">

> Application conçue et codée avec **Claude Opus 5.5** d'Anthropic, dans Claude Code. Logo dessiné avec Codex.

## Ce qu'elle fait

- **Musique :** titre, pochette, égaliseur animé, lecture/pause, piste précédente et suivante. La barre de progression est cliquable, si le lecteur l'accepte, et prend la couleur de la pochette comme l'égaliseur. Au survol, les barres de lecture et de volume montrent la valeur visée avant le clic. Elle suit tout lecteur qui publie ses contrôles média à Windows, comme un navigateur. Un clic sur la pochette ramène le lecteur au premier plan et, dans un navigateur, sélectionne l'onglet qui joue.
- **Notifications :** toutes les applications par défaut (Discord, WhatsApp, Teams, mails...). Les réglages permettent de retirer celles qu'on ne veut pas voir. Auteur, message et image s'affichent 5 secondes. Le nombre de notifications non lues reste visible sur la barre. Plusieurs messages de la même personne tiennent sur une seule ligne, par exemple « Alice (3) ». Effacée ou ouverte depuis l'île, une notification quitte aussi le centre de notifications de Windows. Ouvrir soi-même l'application la marque comme lue, sauf si elle est arrivée pendant que l'application était déjà devant toi.
- **Notifications depuis tes scripts :** `IslandBar.exe --notify "Build terminé"` affiche ton propre message. Voir [Notifications depuis un script](#notifications-depuis-un-script).
- **Micro et caméra :** un point orange pour le micro et un point vert pour la caméra s'affichent quand une application les utilise, comme sur iPhone. Son nom apparaît au survol de la barre.
- **Accès au salon :** cliquer sur une notification Discord ouvre directement son salon ou sa conversation privée. Pour une autre application, le clic ouvre l'application.
- **Volume par application :** le réglage agit seulement sur l'application en lecture, pas sur le son général du PC.
- **Sortie audio :** passe du casque aux enceintes depuis le panneau. Contrairement au volume, ce choix vaut pour tout le PC.
- **Ne pas déranger :** pendant un jeu, une vidéo ou une présentation en plein écran, ou à la demande depuis les réglages, les notifications ne s'affichent plus. Elles sont gardées et reviennent dans le badge à la fin. Pour les garder pendant les jeux, décoche « En plein écran » dans Réglages › Ne pas déranger.
- **Choix de l'écran :** avec plusieurs écrans, les réglages permettent de choisir celui où s'affiche la barre.
- **Barre déplaçable :** maintiens le clic sur la barre, ou sur le fond du panneau, puis fais-la glisser. Sa place est gardée, et dans la moitié basse de l'écran le panneau s'ouvre vers le haut. Posée contre le bord gauche ou droit, elle se met debout, et la bulle comme le panneau s'ouvrent vers l'intérieur, à la hauteur où tu l'as lâchée. Pour la remettre en haut au centre, repose-la près de cet endroit ou choisis son écran dans les réglages.
- **Mises à jour :** IslandBar Lite cherche les nouvelles versions dans les Releases et le signale par une notification. Un clic l'installe et la relance.
- **Discrétion :** sans rien à afficher, la barre est invisible et les clics passent au travers. Elle ne vole jamais le focus, sauf quand tu l'ouvres.
- **Langues :** l'interface est en français si Windows est en français, en anglais sinon.

## Raccourcis

`Ctrl + Alt + ²` ouvre ou ferme le panneau. La touche est celle à gauche de `1`, quelle que soit la disposition du clavier.

| Touche | Action |
|---|---|
| `Espace` | Lecture / pause |
| `←` `→` | Piste précédente / suivante |
| `↑` `↓` | Volume de l'application, par pas de 5 % |
| `S` | Dérouler la liste des sorties audio |
| `Entrée` | Ouvrir la notification la plus récente |
| `Suppr` | Effacer les notifications |
| `Échap` | Fermer le panneau |

Quand la barre est visible, un clic l'ouvre aussi. L'engrenage du panneau, ou un clic droit sur la barre, déroule les réglages : ne pas déranger, écran, applications à afficher et quitter. Une application y apparaît dès sa première notification. Tous ces choix restent enregistrés.

## Notifications depuis un script

Tant qu'IslandBar Lite tourne, n'importe quel script peut lui envoyer un message :

```powershell
& "$env:LOCALAPPDATA\Programs\IslandBar\IslandBar.exe" --notify "Build terminé"
```

Le message s'affiche comme une notification, dans la limite de 300 caractères. Le code de sortie vaut 0 si le message est passé, 1 si IslandBar Lite n'est pas lancée. Seul ton compte Windows peut écrire à la barre.

Pour afficher une image à la place du logo d'IslandBar Lite, ajoute `--icon` juste après `--notify`, suivi d'un PNG ou d'un JPG présent sur ton PC (pas sur un partage réseau) :

```powershell
& "$env:LOCALAPPDATA\Programs\IslandBar\IslandBar.exe" --notify --icon "C:\Images\build.png" "Build terminé"
```

## Installation

Télécharge [`IslandBar-Lite-Setup.exe`](https://github.com/LKDM7/IslandBar-Lite/releases/latest/download/IslandBar-Lite-Setup.exe) depuis les [Releases](https://github.com/LKDM7/IslandBar-Lite/releases), puis lance-le. Il faut seulement Windows 11 (x64) : .NET est inclus et aucun droit administrateur n'est demandé.

L'installateur n'est pas signé. Si Windows SmartScreen s'affiche, clique sur **Informations complémentaires**, puis sur **Exécuter quand même**.

L'assistant propose de lancer IslandBar Lite au démarrage de Windows. La désinstallation se fait depuis Paramètres › Applications et efface aussi le jeton Discord.

## Compiler depuis les sources

Il faut Windows 11 et le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/LKDM7/IslandBar-Lite.git
cd IslandBar-Lite
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

Le résultat est `installer\Output\IslandBar-Lite-Setup.exe`.

### Lancer IslandBar Lite au démarrage de Windows

L'installateur le propose. Sans lui :

```powershell
$exe = (Resolve-Path .\app\IslandBar.exe).Path
$t = New-ScheduledTaskTrigger -AtLogOn -User ([Security.Principal.WindowsIdentity]::GetCurrent().Name); $t.Delay = 'PT10S'
Register-ScheduledTask -TaskName IslandBar -Force -Action (New-ScheduledTaskAction -Execute $exe) -Trigger $t -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries)
```

### Bannières Windows

Pour les applications cochées dans les réglages, IslandBar Lite coupe la bannière Windows tant qu'elle tourne, pour éviter une double notification. Elle la rétablit quand elle se ferme ou qu'on la désinstalle. Une bannière que tu avais déjà coupée dans Windows reste coupée.

## Connexion à Discord

Aucune connexion n'est nécessaire pour ouvrir le bon salon depuis une notification récente. Un clic rejoue le clic Windows sur la notification : Discord ouvre lui-même le salon, sans compte à connecter ni réglage. Discord ferme sa notification au bout de quelques secondes (encore valable à 11 s, plus à 20 s lors du test). Passé ce délai, le clic ouvre simplement Discord.

Pour retrouver le salon d'une notification plus ancienne, IslandBar Lite peut aussi utiliser l'API locale de Discord, car les notifications Windows ne contiennent pas l'identifiant du salon. Au premier lancement, Discord demande alors une autorisation à accepter une seule fois. Si tu la refuses, IslandBar Lite ne la redemande plus : supprime `%APPDATA%\IslandBar\discord.off` pour qu'elle la redemande au prochain lancement.

La connexion passe par l'application Discord définie par `ClientId` dans `DiscordRpc.cs`, en mode **Public Client**, avec la redirection `http://127.0.0.1`. Aucun secret n'est nécessaire.

Discord réserve cette API aux testeurs d'une application non approuvée. Elle reste facultative. Pour en profiter sur un autre compte, il faut soit être ajouté comme testeur, soit créer sa propre application et remplacer `ClientId`.

## Confidentialité

- Seuls deux services sont contactés : `discord.com`, pour obtenir et renouveler le jeton de connexion, et GitHub, pour chercher les mises à jour toutes les 6 heures et les télécharger. Aucune notification ni aucun nom d'application ne leur est envoyé.
- Une mise à jour n'est installée qu'après un clic, et seulement si son empreinte SHA-256 correspond à celle publiée sur GitHub.
- IslandBar Lite n'ouvre jamais le micro ni la caméra : elle demande seulement à Windows quelles applications les utilisent. Elle ne reçoit ni son ni image, et n'enregistre rien de cet usage.
- Au clic sur la pochette, IslandBar Lite lit les titres des onglets du navigateur pour trouver celui qui joue. Ils ne sont ni enregistrés ni envoyés.
- Les notifications restent en mémoire et disparaissent à la fermeture de l'application. Seules celles des applications cochées s'affichent.
- La liste des applications vues et tes choix sont enregistrés dans `%APPDATA%\IslandBar\settings.json`.
- Le jeton est chiffré avec le compte Windows dans `%APPDATA%\IslandBar\discord.token`.
- La base de notifications de Windows est lue uniquement pour récupérer les photos de profil et l'étiquette qui permet de rejouer le clic. Seuls les fichiers locaux sont ouverts.

## Limites connues

- Chrome n'a qu'un volume pour tous ses onglets, donc le réglage les touche tous.
- La ligne de volume n'apparaît pas si l'application en lecture n'est pas reconnue, ce qui peut arriver avec Firefox ou certaines applications du Microsoft Store.
- La barre reste invisible par-dessus les jeux en plein écran exclusif. En fenêtré sans bordure, elle s'affiche.
- Il n'est pas possible de répondre à un message depuis la barre.
- La toute première notification d'une application s'affiche aussi dans Windows : IslandBar Lite ne coupe sa bannière qu'à partir de là.
- Si IslandBar Lite plante ou est arrêtée de force, les bannières Windows restent coupées jusqu'au prochain lancement suivi d'un « Quitter ».
- Hors Discord, le clic sur une notification ouvre l'application, pas le message précis.
- Rejouer le clic passe par un mécanisme interne de Discord, qu'une mise à jour de Discord pourrait changer. Le clic ouvrirait alors simplement Discord.
- Ouvrir soi-même une application du Microsoft Store n'efface pas toujours ses notifications de la barre : `Suppr` les retire.
- Changer la sortie audio passe par une interface Windows non documentée, qu'une mise à jour de Windows pourrait casser.
- Windows ne donne pas l'adresse du morceau : dans une application comme Spotify, le clic sur la pochette la ramène au premier plan sans ouvrir la fiche du morceau. Si le titre de l'onglet ne contient pas celui du morceau, seul le navigateur passe au premier plan.
- Un logiciel qui écoute le micro en arrière-plan, comme un enregistreur de clips, garde le point allumé et donc la barre visible.
- Un micro branché pendant que la barre tourne peut mettre jusqu'à une minute à être suivi.
- Sur les claviers où `AltGr` + la touche à gauche de `1` écrit un caractère, comme `\` sur un clavier espagnol, le raccourci de l'île capte cette combinaison.
- Le point caméra n'a pas encore été essayé avec une vraie webcam.
- Testée uniquement sur un PC sous Windows 11 (build 26200).

## Licence

[MIT](LICENSE).
