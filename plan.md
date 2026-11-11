# Plan d'implémentation — Portage Linux natif de MiFlash

> Référentiel : `spec-linux.md` (même branche `dev`).
> Stratégie : **réutiliser le code protocole, réécrire les bords OS, remplacer l'UI par une CLI**.

---

## 1. Synthèse de la stratégie

| Couche | Décision |
|---|---|
| Protocoles Sahara/Firehose, formats d'images (`code.module`, `FileTransfer`, `Comm`) | **Port direct** (code managé pur, ~95 % réutilisable) |
| Moteur EDL (`SerialPortDevice`) | **Port avec refactor** : extraire `SerialPort` derrière `ITransport` |
| Détection USB (WMI, MiUSB.dll, lsusb.exe, rundll32) | **Réécrire** sur libusb + sysfs + udev |
| Processus externes (`cmd.exe`, .bat, fastboot.exe, fh_loader…) | **Réécrire** : exécuteur POSIX + moteur de script natif |
| Auth SLA (`SLA_Challenge.dll`) | **Remplacer** par `IEdlAuthenticator` (blob statique NoAuth en v1) |
| Drivers (setupapi, Registry) | **Supprimer** (udev rules) |
| MES/usine, Tegra, MTK, OleDb, Vista dialogs | **Écarter** de la v1 |
| UI WinForms | **CLI** (v1) puis **Avalonia** (v2) |

## 2. Jalons

### M0 — Hygiène du dépôt & socle build (≈ 1 j)

- Ajouter `.gitignore` (`bin/`, `obj/`, `*.user`, logs) et **retirer `src/bin`, `src/obj`, logs et exécutables committés (59 Mo)** — conservation des sources C de `fh_loader/QSaharaServer` (référence protocole) et des `.inf` (documentation).
- Créer la solution multi-projets .NET 8 (`MiFlash.Core`, `MiFlash.Cli`, … cf. spec §4).
- CI GitHub Actions `ubuntu-latest` : `dotnet build` + `dotnet test`.
- Convention : branches `feat/<module>`, PR vers `dev`, `main` = tags stables.

### M1 — MiFlash.Core : protocoles (≈ 4 j)

- Copier/porter `XiaoMiFlash.code.module/**` : paquets Sahara (`sahara_*`), commandes
  Firehose XML (`Firehose.cs`), `CommandFormat`, `SoftwareImage`, `Sparse*`, `FlashType`,
  `Storage`, `UserInfo`, `FlashResult`.
- Définir `ITransport` (Open/Close/Read/Write/DiscardBuffers/IsOpen) + `IEdlAuthenticator`.
- Porter `Comm.cs` sur `ITransport` : boucles ACK/NAK, `GetResponse`/`GetResponseXml`,
  détection DDR4/DDR5, chip serial, `edlAuthErr`.
- Porter `FileTransfer.cs` (program/patch par secteurs, sparse, FFU, chunk ACK).
- Encoder en UTF-8 explicite ; remplacer `AppDomain…ApplicationBase` par `IAppPaths` (XDG).
- **Tests unitaires** : paquets golden (HELLO 48 o, READ DATA 64 bits), fixtures
  `rawprogram/patch/provision` XML, images sparse de test, blob auth 256 o.

### M2 — Transport USB libusb (≈ 4 j)

- `MiFlash.Transport.Usb` avec LibUsbDotNet : énumération (VID 05c6:9008 + liste fastboot
  2717/18d1/05c6/8087/0955/31ef), claim interface, bulk IN/OUT, ZLP, timeouts, hotplug.
- Index appareil = (bus, port physique) — remplace `qcGetDeviceIndex`/hub parsing.
- `MiFlash.Transport.Serial` (fallback `qcserial`, `/dev/ttyUSB*`) via `System.IO.Ports`.
- Règles udev packagées + `miflash doctor` (vérifie permissions, libusb, fastboot).
- Test matériel minimal : « HELLO reçu, programmer téléversé » sur un 9008.

### M3 — Moteur EDL complet (≈ 6 j)

- Porter `SerialPortDevice` → `MiFlash.Edl.EdlFlasher` (async/Task + CancellationToken) :
  `SaharaDownloadProgrammer` (avec récupération « switch mode back »), `ConfigureDDR`,
  `dlAuth` (StaticBlobAuthenticator), peek CPUID → choix DDR4/5, `BackupRom`,
  `Provision`, `FirehoseDownloadImg`, `ApplyPatchesToDevice`, `GetSha256Digest`,
  `Firmwarewrite`, `SetBootPartition`, resets.
- Progression par appareil (port de `DummyProgress` + `FlashingDevice` → événements).
- Gestion multi-appareils parallèles (1 Task/appareil), arrêt propre.
- Validation sur la matrice README : SD 636 / 680 / 845 / 855 / 860 / 870.

### M4 — CLI (≈ 3 j)

- `MiFlash.Cli` (System.CommandLine + Spectre.Console) :
  `miflash devices [--json] [--watch]`, `miflash flash <dir> [--programmer f] [--device sN]`,
  `miflash backup/restore`, `miflash provision`, `miflash reboot [--edl]`, `miflash version`.
- Logs : `~/.local/state/miflash/log/<device>@<date>.txt` + log résultat global
  (port de `Log.cs`), drapeaux `--read-dump/--write-dump` équivalents.
- i18n : port de `LanguageProvider` + `LanguageLibrary.xml` (fr/en/zh).
- Sécurité : confirmation avant erase/program, `--yes`, avertissement usage éducatif.

### M5 — Fastboot (≈ 3 j)

- `MiFlash.Fastboot` : détection (process `fastboot devices` + corrélation libusb),
  exécution des scripts : traduction `flash_all*.bat` ↔ `flash_all*.sh` en opérations
  natives (parsing lignes `fastboot flash …`), timeout 11 min 40 s + kill arborescence
  (port de `Cmd.cs`/`MiCmdProcess` en exécuteur POSIX), cas Intel DNX (`fastboot boot loader.efi`).
- `miflash flash-fastboot <rom_dir> [--script auto|all|except-storage|lock]`.

### M6 — Packaging, GUI, durcissement (≈ 5 j)

- Packaging : `dotnet publish` self-contained, `.deb`, `.rpm`, tarball, AUR ;
  installation udev rules ; `miflash doctor`.
- (Optionnel) GUI Avalonia 11 reprenant les écrans utiles : liste appareils + progression
  + sélection répertoire (équivalent `MainFrm`/`ProcessFrm`, sans les menus usine).
- Documentation utilisateur (README-linux.fr/en), matrice matériel testée.
- v2 éventuelle : client fastboot natif libusb, signer SLA alternatif.

## 3. Matrice de migration des fichiers

| Source (Windows) | Destination (port Linux) | Action |
|---|---|---|
| `code.module/**` (57 fichiers) | `MiFlash.Core/Sahara`, `MiFlash.Core/Firehose`, `MiFlash.Core/Images` | Port direct |
| `Comm.cs` | `MiFlash.Core/Comm.cs` (sur `ITransport`) | Port + refactor |
| `FileTransfer.cs` | `MiFlash.Core/FileTransfer.cs` | Port + refactor |
| `SerialPortDevice.cs` | `MiFlash.Edl/EdlFlasher.cs` | Port + async |
| `code.data/*` | `MiFlash.Edl/State/*` | Port direct |
| `UsbDevice.cs`, `TreeViewUsbItem.cs`, `ComPortCtrl.cs`, `DeviceInfo.cs`, `DeviceClasses.cs` | `MiFlash.Transport.Usb/DeviceEnumerator.cs` | Réécriture libusb |
| `Cmd.cs`, `MiCmdProcess.cs`, `MiProcess.cs` | `MiFlash.Fastboot/ProcessRunner.cs` | Réécriture POSIX |
| `Script.cs` (chemins) | `MiFlash.Core/AppPaths.cs` | Réécriture XDG |
| `Log.cs` | `MiFlash.Core/Log.cs` | Port (chemins XDG) |
| `LanguageProvider.cs` + XML | `MiFlash.Core/i18n/` | Port |
| `SoftwareImage.cs` (patterns) | `MiFlash.Core/Images/SoftwareImage.cs` | Port direct |
| `EDL_SLA_Challenge.cs` | `MiFlash.Core/Auth/StaticBlobAuthenticator.cs` | Remplacement (blob managé) |
| `ScriptDevice.cs` | `MiFlash.Fastboot/FastbootFlasher.cs` | Réécriture partielle |
| `MainFrm.cs`, `XiaoMiFlash.form/*`, `MiControl/*`, VistaDialogCore | `MiFlash.Cli` puis `MiFlash.Gui` | Remplacement |
| `MiDriver.cs`, `Driver.cs`, `MiInstaller.cs` | `packaging/udev/51-miflash.rules` | Suppression → udev |
| `Mes.cs`, `X5Mes.cs`, `SocketChatClient.cs`, `EDL_SA_COMMUNICATION.cs`, `ShareMemory.cs`, `FtpHelper.cs`, `SwDownloader.cs`, `Authentication.cs`, `Test.cs` | — | Écartés v1 (drapeaux `#if FACTORY`) |

## 4. Organisation Git / CI

- `dev` : branche d'intégration (déjà créée) ; `feat/*` → PR → `dev` ; `main` reçoit les merges taggés.
- CI : build + tests (ubuntu-latest, .NET 8) ; lint `dotnet format` ; artefact tarball par commit.
- Chaque jalon = tag `v0.x-mN` sur `dev` une fois ses critères d'acceptation (spec §8) passés.

## 5. Planning consolidé

| Jalon | Charge | Cumul |
|---|---|---|
| M0 Hygiène + socle | 1 j | 1 j |
| M1 Core protocoles | 4 j | 5 j |
| M2 Transport USB | 4 j | 9 j |
| M3 Moteur EDL | 6 j | 15 j |
| M4 CLI | 3 j | 18 j |
| M5 Fastboot | 3 j | 21 j |
| M6 Packaging/GUI | 5 j | 26 j |

≈ 4–5 semaines ETP pour la v1 complète (CLI + EDL + fastboot + packaging).

## 6. Risques (extrait spec §9) et mitigations opérationnelles

1. **Accès matériel** : se procurer dès M2 un appareil de test 9008 (matrice README) — sinon M3 reste bloqué en tests golden uniquement.
2. **Timings USB** : instrumenter les ACK groupés (`chkRspAck(chunkCount)`) avant d'optimiser.
3. **Loaders firehose** : ne jamais les committer (licence/usage) ; l'utilisateur fournit `prog_*_firehose_*.elf`.
4. **Encodages** : audit `Encoding.Default` dès M1 (grep systématique).
