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
| M4 | CLI `miflash` | ✅ fait |
| M5 | ROM fastboot (`flash_all*`) | ⏳ |
| M6 | Packaging (deb/rpm/tarball), durcissement | ⏳ |

Détails : [`spec-linux.md`](spec-linux.md) · [`plan.md`](plan.md) · [`todo.md`](todo.md).

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

See the table above (M0→M4 done → M6 planned). Details in
[`spec-linux.md`](spec-linux.md), [`plan.md`](plan.md), [`todo.md`](todo.md)
(French).

### CLI usage (M4)

```bash
miflash doctor                       # environment check
miflash devices [--json] [--watch]   # list EDL 9008 / fastboot devices
miflash flash <dir> [--yes]          # full flash of an image directory
miflash backup <dir>                 # read-only backup (rawprogram_backup.xml)
miflash restore <dir> --from <dir>   # restore a backup session
miflash provision <dir>              # UFS provisioning (provision*.xml)
miflash reboot [dir] [--edl]         # reboot, or stay in EDL 9008 mode
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
