# MiFlash — Portage natif Linux

> **FR** · [English below](#english)

## ⚠️ Avertissement (lire en premier)

Outil à **usage éducatif uniquement**. Le flash EDL écrase la mémoire de
l'appareil et peut le rendre **définitivement inutilisable (brick)**. Ce projet
n'est ni affilié à Xiaomi ni à Qualcomm. Vous êtes seul responsable de ce que
vous flashez. Les loaders firehose (`prog_*_firehose_*.elf`) ne sont pas
distribués ici : vous devez fournir les vôtres.

## État du portage

| Jalon | Contenu | État |
|---|---|---|
| M0 | Hygiène du dépôt, solution .NET 8, CI + release | ✅ fait |
| M1 | `MiFlash.Core` : protocoles Sahara/Firehose, `ITransport`, auth NoAuth, tests golden | ✅ fait (tag `v0.1-m1`) |
| M2 | Transport USB libusb (9008), udev, hotplug | ✅ fait (sauf test matériel M2.7) |
| M3 | Moteur EDL complet (rawprogram/patch, provision, backup, multi-appareils) | ✅ fait (tag `v0.2-m3`, sauf matrice matérielle M3.10) |
| M4 | CLI `miflash` | ✅ fait (tag `v0.3-m5`) |
| M5 | ROM fastboot (`flash_all*`) | ✅ fait (tag `v0.3-m5` : moteur natif .bat/.sh, timeout 11 min 40 s, Intel DNX) |
| M6 | Packaging (deb/rpm/AUR/tarball), docs, audit | ✅ fait (tag `v1.0.0`) |

Détails : [`spec-linux.md`](spec-linux.md) · [`plan.md`](plan.md) · [`todo.md`](todo.md).

## Installation (v1.0.0+)

Artefacts de chaque release : tarballs `linux-x64/arm64`, paquets `.deb` et
`.rpm` (x86_64/aarch64), `SHA256SUMS`.

**Debian / Ubuntu / dérivés** — téléchargez `miflash-linux_<version>_amd64.deb`
puis :

```bash
sudo apt install ./miflash-linux_1.0.0_amd64.deb   # installe aussi les règles udev
miflash doctor                                     # vérifie l'environnement
```

**Fedora / RHEL / openSUSE** — `miflash-linux-<version>.x86_64.rpm` :

```bash
sudo dnf install ./miflash-linux-1.0.0.x86_64.rpm
```

**Arch Linux (AUR)** — le [`PKGBUILD`](packaging/aur/PKGBUILD) construit
depuis le tag git :

```bash
yay -S miflash-linux        # ou : git clone aur@aur.archlinux.org:miflash-linux.git
```

**Tarball (toute distribution)** :

```bash
tar -xzf miflash-linux-x64.tar.gz && cd miflash-linux-x64
sudo cp packaging/udev/51-miflash.rules /usr/lib/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
sudo ln -s "$PWD/miflash" /usr/local/bin/miflash
```

Les paquets installent l'application self-contained dans `/usr/lib/miflash-linux`
avec un lien `/usr/bin/miflash`, les règles udev
(`/usr/lib/udev/rules.d/51-miflash.rules`), puis rechargent udev
automatiquement (postinstall). Aucune dépendance runtime n'est requise
(.NET 8 embarqué) ; `fastboot` (paquet `android-tools`) reste optionnel pour
`miflash flash-fastboot`.

### Prérequis

| Composant | Rôle | Obligatoire |
|---|---|---|
| libusb-1.0-0 | transport EDL 9008 | oui (détecté par `miflash doctor`) |
| règles udev 51-miflash.rules | accès sans root aux 9008/fastboot | fortement conseillé (sinon `sudo`) |
| fastboot (`android-tools`) | `miflash flash-fastboot` | pour les ROM fastboot uniquement |
| loaders `prog_*_firehose_*.elf` | programmer firehose | à fournir dans le répertoire d'images |

Vérification en une commande : `miflash doctor` (libusb, udev, fastboot,
répertoires XDG, runtime).

## Utilisation du CLI (M4)

```bash
# Diagnostics d'environnement (libusb, udev, fastboot, .NET)
miflash doctor

# Lister les appareils EDL 9008 / fastboot (JSON machine ou surveillance)
miflash devices [--json] [--watch]

# Flash complet d'un répertoire d'images (programmers + rawprogram/patch XML)
miflash flash <dir> [--programmer auto|lite|ddr4|ddr5] [--device 1.3]… [--yes]

# Sauvegarder les partitions listées par rawprogram_backup.xml (lecture seule)
miflash backup <dir>            # → ~/.local/share/miflash/restore/<session>_<chip>

# Restaurer une sauvegarde
miflash restore <dir> --from <session_de_sauvegarde>

# Provisionner le stockage UFS (provision*.xml)
miflash provision <dir>

# Redémarrer la cible (ou la laisser en mode EDL 9008)
miflash reboot [dir] [--edl]

# Flash d'une ROM fastboot via flash_all*.bat/.sh (moteur natif, sans shell)
miflash flash-fastboot <rom> [--script auto|all|except-storage|lock] [--device serial]… [--yes]
```

Options communes : `--device` (répétable, par localisation `bus.port` ou numéro
de série ; défaut : tous les 9008), `--serial /dev/ttyUSB0` (fallback qcserial),
`--read-dump/--write-dump` (trames hexadécimales), `--backup-dir`, `--quiet`,
`--yes` (confirme erase/program ; sans terminal, la confirmation interactive
est requise), `--lang fr|en|zh`.

Journaux par appareil : `~/.local/state/miflash/log/<device>@<date>.txt`,
déplacés en `<yyyyMd>/pass|fail/` à la fin de l'opération, plus un journal
global `result.log`.

Interface linguistique : bibliothèque embarquée (fr/en/zh), surcharge
utilisateur dans `~/.config/miflash/LanguageLibrary.xml`.

Codes de sortie : 0 succès · 1 échec/annulation · 2 erreur d'usage · 3 aucune
cible EDL · 4 échec doctor.

## Matrice matérielle

Cibles prévues par la spécification (programmers DDR4/DDR5 via peek CPUID) —
validation **en attente de matériel** (M2.7/M3.10) :

| SoC | Programmer attendu | Statut |
|---|---|---|
| Snapdragon 636 / 660 (SDM636/660) | `prog_ufs_firehose_sm8250_ddr_*.elf` | ⏳ non testé |
| Snapdragon 680 (SM6225) | `prog_firehose_lite.elf` / ddr_4 | ⏳ non testé |
| Snapdragon 845 (SDM845) | `prog_ufs_firehose_sm8250_ddr_4.elf` | ⏳ non testé |
| Snapdragon 855 (SM8150) | `prog_ufs_firehose_sm8250_ddr_5.elf` | ⏳ non testé |
| Snapdragon 860 / 870 (SM6125/SM8250) | `prog_ufs_firehose_sm8250_ddr_4/5.elf` | ⏳ non testé |
| Autres SoC Qualcomm 9008 | `prog_firehose_lite.elf` (repli) | ⏳ non testé |

Chaque flash réussi sur un appareil réel est le bienvenu : ouvrez une issue
avec le modèle exact, le contenu de `miflash doctor`, le log
`~/.local/state/miflash/log/…/pass/` et le programmer utilisé, afin
d'alimenter cette matrice.

## Dépannage

| Symptôme | Piste |
|---|---|
| `error: only nop and sig tag…` | auth NoAuth non déclenchée → vérifier `StaticBlobAuthenticator` (taille 256 o annoncée via `AUTHP`) |
| Pas de ACK après `<configure>` | payload trop grand (NAK 16384) → déjà géré comme `Comm.GetResponse` l. 374 |
| HELLO jamais reçu | port 9008 non ouvert (permissions udev) ou interface non claimée → `miflash doctor` |
| `no EDL device found` | règles udev non rechargées / câble de données requis / appareil pas en mode 9008 (Vol- + câble) |
| Mauvais programmer | fichier ne matchant pas `prog_.*_firehose_.*.elf` / DDR choisi → `--programmer` explicite |
| `fastboot` introuvable | installer `android-tools` (paquet distribution) |
| Flash fastboot interrompu | timeout global 11 min 40 s — vérifier câble/port USB, relancer |

## Démarrage rapide (développeur)

```bash
# Prérequis : .NET SDK 8, libusb-1.0-0
cd miflash-linux
dotnet build MiFlash.Linux.sln
dotnet test  MiFlash.Linux.sln
```

La CI (GitHub Actions, `ubuntu-latest`) build + teste chaque push : voir
`.github/workflows/ci.yml`. Les tags `v*` déclenchent la release avec tarballs
self-contained linux-x64/arm64 + `SHA256SUMS` : `.github/workflows/release.yml`.

## Structure du dépôt

```
miflash-linux/   solution .NET 8 du port Linux (code actif)
src/             code C#/WinForms Windows historique (référence, ne pas modifier)
reference/       sources C Qualcomm (fh_loader/QSaharaServer), .inf, LanguageLibrary.xml
spec-linux.md    spécification du portage (dépendances Windows → Linux)
plan.md          plan d'implémentation M0→M6
todo.md          checklist exécutable
```

---

## English

### ⚠️ Warning (read first)

**Educational use only.** EDL flashing overwrites device storage and can
**permanently brick** your device. This project is not affiliated with Xiaomi
or Qualcomm. You are solely responsible for what you flash. Firehose loaders
(`prog_*_firehose_*.elf`) are **not** distributed here — bring your own.

### Status

See the table above (M0→M6 done). Details in [`spec-linux.md`](spec-linux.md),
[`plan.md`](plan.md), [`todo.md`](todo.md) (French).

### Installation (v1.0.0+)

Each release ships tarballs, `.deb` and `.rpm` packages (x86_64/aarch64) and
`SHA256SUMS`.

```bash
# Debian/Ubuntu
sudo apt install ./miflash-linux_1.0.0_amd64.deb
# Fedora/RHEL/openSUSE
sudo dnf install ./miflash-linux-1.0.0.x86_64.rpm
# Arch (AUR, builds from the git tag)
yay -S miflash-linux
# any distro (tarball)
tar -xzf miflash-linux-x64.tar.gz
```

Packages install the self-contained app to `/usr/lib/miflash-linux` with a
`/usr/bin/miflash` symlink, drop the udev rules
(`/usr/lib/udev/rules.d/51-miflash.rules`) and reload udev automatically.
No runtime dependency (.NET 8 embedded); `fastboot` (`android-tools`)
remains optional for `miflash flash-fastboot`. Check everything with
`miflash doctor`.

### Hardware matrix

Targets from the spec (DDR4/DDR5 programmers via CPUID peek) — hardware
validation **pending** (M2.7/M3.10). Please report successful flashes
(open an issue with device model, `miflash doctor` output and per-device log)
to feed the matrix in the French section above.

### CLI usage (M4)

```bash
miflash doctor                       # environment check
miflash devices [--json] [--watch]   # list EDL 9008 / fastboot devices
miflash flash <dir> [--yes]          # full flash of an image directory
miflash backup <dir>                 # read-only backup (rawprogram_backup.xml)
miflash restore <dir> --from <dir>   # restore a backup session
miflash provision <dir>              # UFS provisioning (provision*.xml)
miflash reboot [dir] [--edl]         # reboot, or stay in EDL 9008 mode
miflash flash-fastboot <rom>          # fastboot ROM via flash_all* (native engine)
```

Common options: `--device` (repeatable, by `bus.port` location or serial;
default: all 9008), `--serial /dev/ttyUSB0`, `--read-dump/--write-dump`,
`--backup-dir`, `--quiet`, `--yes`, `--lang fr|en|zh`.

Per-device logs live in `~/.local/state/miflash/log/` (moved to
`<yyyyMd>/pass|fail/` after each operation, plus a global `result.log`).

### Quick start (developer)

```bash
# Prerequisites: .NET SDK 8, libusb-1.0-0
cd miflash-linux
dotnet build MiFlash.Linux.sln
dotnet test  MiFlash.Linux.sln
```

CI (GitHub Actions, `ubuntu-latest`) builds and tests every push:
`.github/workflows/ci.yml`. Tags `v*` trigger the release workflow with
self-contained linux-x64/arm64 tarballs + `SHA256SUMS`.
