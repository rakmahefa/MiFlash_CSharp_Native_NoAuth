# Guide d'utilisation — MiFlash Linux GUI (`miflash-gui`)

> Interface graphique Avalonia 11 du portage Linux (M6.3). Le GUI pilote
> **exactement le même moteur** que le CLI (`miflash`) : multi-appareils,
> progression par appareil, journaux de session XDG. Aucune fonctionnalité
> n'est exclusive à l'un ou l'autre — choisissez selon votre confort.
>
> ⚠️ **Usage éducatif** : le flash EDL **efface tout le stockage** et une
> erreur peut bricker l'appareil définitivement. Aucune affiliation
> Xiaomi/Qualcomm. Vous êtes seul responsable de vos opérations.

## 1. Installation

Le GUI est un **paquet complémentaire** du paquet principal — installez les
deux (le paquet GUI dépend du paquet CLI, qui fournit les règles udev et la
documentation commune) :

```bash
# Debian/Ubuntu (amd64 — arm64 disponible)
sudo apt install ./miflash-linux_1.0.1_amd64.deb
sudo apt install ./miflash-linux-gui_1.0.1_amd64.deb

# Fedora/RHEL (x86_64)
sudo dnf install ./miflash-linux-1.0.1.x86_64.rpm ./miflash-linux-gui-1.0.1.x86_64.rpm

# Arch (AUR)
yay -S miflash-linux miflash-linux-gui
```

Autonomous (« self-contained ») : **aucun runtime .NET à installer**. Le GUI
n'a pas de dépendance graphique particulière (toolkit Avalonia embarqué,
rendu OpenGL/Vulkan natif).

**Tarball manuel** (les deux archives, même version) :

```bash
tar -xzf miflash-linux-x64.tar.gz -C ~/miflash
tar -xzf miflash-gui-x64.tar.gz -C ~/miflash
~/miflash/miflash-linux-gui/miflash-gui
# règles udev à installer à la main (cf. README-linux § Installation)
```

## 2. Premier lancement

1. **Vérifiez l'environnement** avant tout branchement :
   ```bash
   miflash doctor
   ```
   ou directement dans le GUI : bouton **« Vérifier l'environnement (doctor) »**
   — le rapport s'affiche dans le journal (tout doit être `OK`/`WARN`, aucun `FAIL`).

2. **Lancez le GUI** depuis le menu applicatif (catégorie *Système*, « MiFlash »)
   ou en terminal :
   ```bash
   miflash-gui
   ```

3. Après l'installation des paquets, **(re)branchez l'appareil** pour que les
   règles udev s'appliquent (accès sans root aux appareils 9008/fastboot).

> **Wayland** : le GUI tourne aussi bien sous X11 que Wayland (via XWayland,
> automatique). Aucun repli manuel n'est nécessaire en pratique.

## 3. Tour de l'interface

```
┌────────────────────────────────────────────────────────────────────┐
│ ⚠ bandeau d'avertissement éducatif                                 │
├──────────────────┬─────────────────────────────────────────────────┤
│ Appareils        │ Répertoire ROM      [ .................... ] [Parcourir…] │
│ [Rafraîchir]     │ Programmer EDL      [auto ▾]                       │
│ ☐ Surveiller     │ ☐ Je comprends : flash/provision effacent…       │
│                  │ [⚡ Flash EDL (9008)]                              │
│ 9008@3.2         │ [Flash fastboot — ROM complète]                  │
│ ▓▓▓▓░░░ 40 %     │ [Flash fastboot — sans effacer le stockage]      │
│ flashing…        │ [Sauvegarde des partitions]                      │
│                  │ [Redémarrer l'appareil]                          │
│                  │ ─────────────────────                            │
│                  │ [Vérifier l'environnement (doctor)]              │
├──────────────────┴─────────────────────────────────────────────────┤
│ Journal (2000 dernières lignes, horodatées)                        │
└────────────────────────────────────────────────────────────────────┘
```

- **Colonne gauche — appareils** : chaque appareil détecté (EDL 9008 ou
  fastboot) avec sa propre barre de progression et son statut. **Sélectionner**
  un appareil cible uniquement celui-ci ; ne rien sélectionner d'EDL cible
  **tous les 9008** présents.
  - *Rafraîchir* : re-scan USB manuel.
  - *Surveiller (hotplug)* : re-scan automatique au branchement/débranchement
    (toutes les 2 s).
- **Colonne droite — opération** :
  - *Répertoire ROM* : racine de la ROM (fastboot ou paquet EDL). L'outil
    descend automatiquement dans le sous-dossier `images/` s'il existe — le
    programmer `prog_*_firehose_*.elf` doit s'y trouver, à côté des
    `rawprogram*.xml`.
  - *Programmer EDL* : `auto` (défaut — prend le firehose présent, détecte
    DDR4/DDR5 tout seul si les deux variants y figurent), ou forçage
    `lite`/`ddr4`/`ddr5`.
  - *Case de confirmation* : équivalent GUI du « yes » du CLI — **obligatoire**
    pour les opérations destructives (flash EDL, flash fastboot).
- **Journal** : messages du moteur (mêmes libellés que le CLI) + verdicts
  finaux. Le journal complet est écrit sur disque (cf. §6).

Pendant une opération, les boutons sont désactivés (`IsBusy`) — patientez ;
le flash EDL dure typiquement 5–15 min par appareil.

## 4. Flasher en EDL (9008) — exemple POCO F3

Prérequis détaillés (mode EDL, câble, anti-rollback) :
**[GUIDE-POCO-F3-EDL.md](GUIDE-POCO-F3-EDL.md)** — la marche à suivre
matérielle est identique, seule la saisie change.

1. Préparez le répertoire ROM : programmer firehose `.elf` **dans le dossier
   `images/`** de la ROM, à côté des `rawprogram*.xml`/`patch*.xml`.
2. Passez le téléphone en mode 9008 (écran noir, `05c6:9008` détecté).
3. Dans le GUI : **Rafraîchir** → l'appareil `9008@bus.port` apparaît.
4. Renseignez le **répertoire ROM** (bouton *Parcourir…*).
5. Laissez *Programmer* sur `auto`.
6. Cochez la **case de confirmation**.
7. Cliquez **⚡ Flash EDL (9008)**.

Déroulé visible dans le journal et les barres : *open port* → Sahara (envoi
du programmer) → configure DDR (+ auth NoAuth si exigée) → provision UFS si
`provision*.xml` → écriture des partitions (`rawprogram`) → `patch` →
**redémarrage automatique** de l'appareil en fin de flash.

> Depuis le correctif v1.0.1, l'ouverture du port USB est gérée par le
> moteur lui-même (le GUI et le CLI livrent un transport fermé au moteur,
> qui l'ouvre) — l'erreur historique « port … is not open » ne peut plus se
> produire ; en cas d'échec d'ouverture, le message réel s'affiche
> (permissions udev, appareil occupé, etc.).

## 5. Autres opérations

| Bouton | Effet | Équivalent CLI |
|---|---|---|
| ⚡ Flash EDL (9008) | flash complet EDL (partitions + patch + reset) | `miflash flash <dir>` |
| Flash fastboot — ROM complète | parse et rejoue `flash_all` (natif, aucun script exécuté) | `miflash flash-fastboot <dir>` |
| Flash fastboot — sans effacer le stockage | variante `except-storage` (conserve données) | `miflash flash-fastboot <dir> --script except-storage` |
| Sauvegarde des partitions | lit les partitions de `rawprogram_backup.xml` | `miflash backup <dir>` |
| Redémarrer l'appareil | `partial_reset` (redémarre la cible) | `miflash reboot` |
| Vérifier l'environnement | diagnostic complet (runtime, libusb, udev, fastboot…) | `miflash doctor` |

Notes :

- **Flash fastboot** : le répertoire doit contenir `flash_all*.sh|.bat`
  (ROM fastboot officielle). L'appareil doit être en mode fastboot
  (Vol– + Vol+ à l'allumage). La sélection fastboot suit la même règle :
  appareil sélectionné si c'est un fastboot, sinon tous les fastboot détectés.
- **Sauvegarde** : les images lues sont rangées sous
  `~/.local/state/miflash/backup/` (voir journal pour le chemin exact).
- **Redémarrer** : nécessite aussi un répertoire ROM valide (le moteur
  rouvre une session firehose pour émettre le `partial_reset`).

## 6. Journaux et rapports

- **Vue GUI** : journal intégré (2000 dernières lignes).
- **Sur disque** (identique au CLI) :
  ```
  ~/.local/state/miflash/log/<yyyyMd>/pass|fail/9008@<date>.txt   # par appareil
  ~/.local/state/miflash/log/result.log                           # verdict global
  ```
- En cas de souci, joignez ces fichiers à un rapport d'issue, avec la sortie
  doctor et éventuellement un flash relancé en CLI avec
  `--read-dump --write-dump` (trames hexadécimales).

## 7. Dépannage

| Symptôme | Piste |
|---|---|
| Aucun appareil dans la liste | mode EDL mal engagé (écran noir ≠ 9008), câble de charge, udev — `doctor`, rebrancher, *Rafraîchir* |
| `répertoire ROM invalide` | chemin inexistant / non renseigné ; le dossier `images/` doit contenir `rawprogram*.xml` |
| `can not found programmer file` | le `.elf` firehose n'est pas dans le dossier `images/` (recherche **non récursive**) |
| `opération destructive : cochez la case…` | case de confirmation non cochée (équivalent `--yes`) |
| `fastboot introuvable sur le PATH` | `sudo apt install fastboot` (Debian) / `android-tools` (Arch/Fedora) — puis re-cliquer |
| Échec configure / DDR | mauvaise génération DDR → mettre `ddr_4` **et** `ddr_5` dans `images/`, laisser `auto`, relancer |
| Interface figée pendant un flash | normale : opérations en tâche de fond, barres/journal actifs ; ne pas débrancher l'appareil |
| Le GUI ne démarre pas (tarball) | vérifier les permissions exécutables (`chmod +x miflash-gui`) et l'archive adaptée à votre architecture |

## 8. Désinstallation

```bash
sudo apt remove miflash-linux-gui miflash-linux      # Debian/Ubuntu
sudo dnf remove miflash-linux-gui miflash-linux      # Fedora/RHEL
yay -R miflash-linux-gui miflash-linux               # Arch
```

Les journaux/sauvegardes (`~/.local/state/miflash/`) sont conservés —
supprimez le dossier manuellement si besoin.

---

*Outil fourni à des fins éducatives, sans affiliation Xiaomi/Qualcomm.
Les loaders firehose ne sont pas distribués avec l'outil.*
