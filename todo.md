# TODO — Portage Linux natif de MiFlash

> Checklist exécutable liée à `plan.md` (jalons M0→M6) et `spec-linux.md` (exigences RF/RNF).
> Cocher avec `git commit` référençant l'ID de la tâche (ex. `feat(core): M1.3`).

## État initial (fait)

- [x] A0.1 Analyse complète du code source (120 fichiers C#, ~14 350 lignes) — commit base `5450f4f`
- [x] A0.2 Inventaire des dépendances Windows (P/Invoke ×80+, WMI, Registry, setupapi, cmd.exe, MiUSB.dll, SLA_Challenge.dll…) → spec §3
- [x] A0.3 Identification du flux EDL complet (Sahara → Firehose → auth NoAuth blob 256 o → rawprogram/patch) → spec §2.2
- [x] A0.4 Création de la branche `dev` + rédaction `spec-linux.md`, `plan.md`, `todo.md`

## M0 — Hygiène du dépôt & socle build

- [x] M0.1 Écrire `.gitignore` (bin/, obj/, *.user, *.suo, log/, *.pdb) et supprimer `src/bin` (59 Mo), `src/obj`, `*.csproj.user` du dépôt — commit `0f5c9e9`
- [x] M0.2 Conserver : sources C de `ThirdParty/Qualcomm/fh_loader/QSaharaServer` (référence), `.inf` (doc), `LanguageLibrary.xml` — extraits vers `reference/` (commit `0f5c9e9`)
- [x] M0.3 Créer la solution .NET 8 multi-projets : `MiFlash.Core`, `MiFlash.Edl`, `MiFlash.Transport.Usb`, `MiFlash.Transport.Serial`, `MiFlash.Fastboot`, `MiFlash.Cli` — commit `36f046d` (LibUsbDotNet 3.0.224, System.IO.Ports 9.0.0, Directory.Build.props, 3 tests fumée)
- [x] M0.4 CI GitHub Actions (ubuntu-latest) : `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` + artefact publish linux-x64 — commit `cc44780` (`.github/workflows/ci.yml`)
- [x] M0.5 README-linux (fr/en) : usage éducatif, avertissement, prérequis — commit `cc44780` (+ `miflash-linux/README.md` guide dev, `.editorconfig`)
- [x] M0.6 Workflow release (`.github/workflows/release.yml`) : tag `v*` → tests + publish self-contained linux-x64/arm64 + tarballs + `SHA256SUMS` + GitHub Release avec artefacts ; `workflow_dispatch` pour build manuel — **validé en CI** (tag `v0.2-m3`, run 42962696046 : release publiée avec tarballs x64 30,9 Mo / arm64 29,6 Mo + SHA256SUMS)

## M1 — MiFlash.Core (protocoles, OS-agnostique)

- [x] M1.1 Porter `XiaoMiFlash.code.module/**` : paquets Sahara (`Sahara/SaharaPackets.cs`), commandes Firehose XML (`Firehose/FirehoseCommands.cs`), `CommandFormat` (`Protocol/`), `SoftwareImage`, `Sparse*`, `FlashType`, `Storage`, modèles (`Models/`) — écarts documentés : `Device.cs` (→ M3, session), `Script.cs` (→ AppPaths M1.6), `CommandPacket`/`StreamingArg`/`HelloCommand`/`StreamWrite*`/`OpenMultiImage*`/`Simple*`/`SecurityMode*`/`MiProperty`/`UserInfo` orphelins ou usine (hors périmètre v1)
- [x] M1.2 Définir `ITransport` (Open/Read/Write/Close/DiscardBuffers/IsOpen/PortName) et `IEdlAuthenticator` (+ `StaticBlobAuthenticator` : blob 256 o extrait programmatiquement de `dlAuth()`, SHA-256 golden `de84d23c…e739`)
- [x] M1.3 Porter `Comm.cs` sur `ITransport` : ACK/NAK (`chkRspAck` simple + chunkCount), `GetResponse`/`GetResponseXml`, détection DDR4/DDR5 (peek `0xC3 0x00 …`), chip serial, `needEdlAuth` — `Protocol/Comm.cs`
- [x] M1.4 Porter `FileTransfer.cs` : `WriteFile`/`ReadFile`/`WriteSparseFileToDevice`/`WriteFfuFile`/`SetBytesToFile` (+ quick transfer `MemImg`), chunk ACK — `Transfer/FileTransfer.cs` (paramètre `Comm` au lieu de `SerialPortDevice`)
- [x] M1.5 Remplacer `Encoding.Default` par UTF-8 explicite (audit : `Comm.SendCommand`) + abstractions `Log`/`FlashStatus` remplaçant `Log.cs`/`FlashingDevice` (UI-dépendants)
- [x] M1.6 `AppPaths` XDG (`~/.local/share|state/miflash/`) remplaçant `AppDomain…ApplicationBase + "Source\\…"` — `Io/AppPaths.cs` (évalué à chaque accès, testable)
- [x] M1.7 Tests unitaires : 64 tests verts — golden packets (HELLO 48 o, HELLO_RESPONSE v2/mode 3, READ DATA 32/64 bits), gabarits Firehose (configure/program/patch/auth), image sparse construite en test (RAW/FILL/DONTCARE), blob auth 256 o (SHA-256 + clone défensif), patterns `SoftwareImage`, comportements `Comm` via `FakeTransport` (ACK groupés, DDR4/5, chip serial, NAK 16384), XDG
- [x] M1.8 Tag `v0.1-m1` — CI verte (run 42951282098), tag poussé

## M2 — Transport USB (libusb) + détection

- [x] M2.1 Intégrer LibUsbDotNet 3.0.224 ; énumération des 9008 (VID `05c6:9008`) + appareils fastboot (VIDs `2717`, `18d1`, `05c6`, `8087`, `0955`, `31ef` ; produit `android`/`fastboot`/`intel android ad` ; fabricant `xiaomi inc`) — `UsbDeviceEnumerator` + `DeviceFilter` (logique fidèle à `UsbDevice.GetAndroidDevices` l.248, Manufacturer non nul requis)
- [x] M2.2 `UsbTransport : ITransport` : claim interface 0, découverte auto des endpoints bulk IN/OUT, ZLP (`NeedsZeroLengthPacket`), timeouts, `DiscardBuffers` (ReadFlush + tampon interne émulant `BytesToRead`)
- [x] M2.3 Index appareil = (bus, port physique) via `UsbDeviceIdentity.Location` — remplace `rundll32 qcCoInstaller` et parsing hub MiUSB
- [x] M2.4 Hotplug insert/remove — `UsbDeviceWatcher` (polling 1 s + diff ; hotplug natif libusb prévu au durcissement M6)
- [x] M2.5 `MiFlash.Transport.Serial` fallback (`qcserial` → `/dev/ttyUSB*`) via `System.IO.Ports` — `SerialTransport`
- [x] M2.6 Règles udev (`packaging/udev/51-miflash.rules` : 05c6:9008 + 2717/18d1/05c6/8087/0955/31ef, `MODE="0660"`, `TAG+="uaccess"`) + doc installation dans le fichier
- [ ] M2.7 Test matériel : HELLO reçu + programmer téléversé sur un 9008 réel — **en attente de matériel** (appareil de la matrice README requis)

## M3 — Moteur EDL complet

- [x] M3.1 Porter `SaharaDownloadProgrammer` (avec boucle de récupération « switch mode back » l. 390–444) — `EdlFlasher.SaharaDownloadProgrammer()` (synchrone fidèle à l'original ; async reporté au durcissement M6)
- [x] M3.2 `ConfigureDDR` + gestion `needEdlAuth` → `DlAuth()` via `IEdlAuthenticator`/`StaticBlobAuthenticator` (blob 256 o de `dlAuth()`)
- [x] M3.3 Peek CPUID → sélection programmer lite/DDR4/DDR5 (`prog_firehose_lite.elf`, `prog_ufs_firehose_sm8250_ddr_4/5.elf`) + détection UFS/eMMC par nom de fichier + re-Sahara après `reset_to_edl` si le programmer détecté diffère
- [x] M3.4 `BackupRom` / restore (`rawprogram_backup/restore.xml`) + `NeedBackupRom/NeedRestoreRom` — `BackupRom`/`ReadFilesFromDevice` (lectures `<read>` rawmode, répertoire session `RestoreDirectory` = `<RestoreRoot>/restore/<yyyyMdHms>_<chipNum>`, défaut XDG `~/.local/share/miflash`, surcharge `RestoreRoot` pour tests/CLI) ; restore intégré en fin de `Flash()` (écriture depuis le répertoire de sauvegarde)
- [x] M3.5 `Provision` — éléments `<ufs>` renvoyés sans `desc` (+`verbose`), SkipStorageInit=1→0, boucle fidèle `XiaomiFlashNew` l.132–201 : partial_reset (quick_reset) ou reset_to_edl + re-Sahara, ping + re-configure après provision ; `uploadDebugpolicy` (usine) documenté no-op v1
- [x] M3.6 `FirehoseDownloadImg` : rawprogram*.xml → programs par secteurs (sparse via `CekSparse` + `WriteSparseFileToDevice`, bruts via `WriteFile`), progression (`FlashStatus`)
- [x] M3.7 `ApplyPatchesToDevice` : patch*.xml → `<patch>` (port fidèle l.1448–1530, correctifs « Fix Bugs at PATCH Operations » inclus)
- [x] M3.8 `GetSha256Digest` (+`GetSha256`), `Firmwarewrite` — `SaveFfu.ffuList`/`MiAppConfig` remplacés par `FfuList`/`FfuDirectory` (défaut XDG `…/miflash/ffu`), `MiFlashGlobal.IsFirmwarewrite` → propriété `IsFirmwarewrite` ; ✅ `SetBootPartition` (UFS), `Reboot` (partial_reset/reset_to_edl portés)
- [x] M3.9 Multi-appareils parallèles — `MultiDeviceFlasher` (+`EdlJob`/`EdlJobResult`) : 1 Task/appareil, `MaxDegreeOfParallelism` (SemaphoreSlim, défaut 4), CancellationToken, un échec n'arrête pas les autres, résultats dans l'ordre de soumission
- [ ] M3.10 Validation matérielle matrice README : SD 636 / 680 / 845 / 855 / 860 / 870 → rapport de test dans `docs/` — **en attente de matériel**
- [x] M3.11 Tag `v0.2-m3` — CI verte (run 42962470543) + release workflow validé (run 42962696046) : GitHub Release `v0.2-m3` avec `miflash-linux-x64.tar.gz`, `miflash-linux-arm64.tar.gz`, `SHA256SUMS`

> Tests d'intégration sans matériel (RNF-5) : `ScriptedEdlDevice` simule une cible
> 9008 (HELLO → READ DATA → DONE, ACK/NAK firehose, NAK d'auth NoAuth, peek
> DDR4/5, read rawmode pour backup, getStorageInfo/inquiry UFS, quick_reset,
> double ACK program/firmwarewrite) — **92 tests verts** couvrant le flux complet
> M3.1→M3.9 (backup → provision → restore, firmwarewrite/FFU, sha256, parallèle).
> ⚠ Le statut « provisioning » se vérifie par prédicat (`Assert.Contains(list, s => …)`) :
> la surcharge `Contains(string, List<string>)` est une égalité exacte d'élément.

## M4 — CLI

- [x] M4.1 Squelette `miflash` (System.CommandLine **2.0.0 GA** + Spectre.Console 0.49.1) : `devices|flash|backup|restore|provision|reboot|doctor|version` — `MiFlash.Cli/Program.cs` + `Commands/` (une classe par commande), codes de sortie 0/1/2/3/4 ; `EdlOperation` (Flash/Backup/Restore/Provision/Reboot/RebootToEdl) ajouté à `MultiDeviceFlasher` + `EdlFlasher.BeginSession()/BackupOnly()/RestoreOnly()/ProvisionOnly()/Reboot()` — toutes les opérations passent par le runner parallèle M3.9
- [x] M4.2 `miflash devices [--json] [--watch]` (RF-1/RF-2) — table Spectre (kind/usb/location/serial/product), JSON camelCase, watch = `UsbDeviceWatcher` + événements `[+] arrivé / [-] retiré` (Ctrl+C)
- [x] M4.3 `miflash flash <dir> [--programmer auto|lite|ddr4|ddr5] [--device bus.port|sn]… [--serial /dev/ttyUSB*] [--read-dump] [--write-dump] [--backup-dir] [--quiet]` (RF-3→RF-11) — `EdlTargets` (résolution `--device` par Location/bus.port/serial, défaut tous les 9008), `EdlRun` (pipeline cibles → jobs → MultiDeviceFlasher → résultats), statuts Spectre via `ConsoleReporter` (`IFlashStatusReporter`), dump = `Comm.isReadDump/isWriteDump`
- [x] M4.4 Confirmations erase/program (`--yes`), avertissement usage éducatif — flash/restore/provision destructifs : avertissement i18n + cibles + saisie « yes » ; stdin non terminal sans `--yes` → refus (scripts explicites) ; backup/reboot non destructifs sans confirmation
- [x] M4.5 Logs par appareil `~/.local/state/miflash/log/` + log résultat global (port `Log.cs`) — `SessionLog` branché sur `Log.Sink` : `<device>@<yyyyMMdd-HHmmss>.txt`, déplacement `<yyyyMd>/pass|fail/` (port de `moveLog`), `result.log` global (PASS/FAIL/device/détail)
- [x] M4.6 i18n : port `LanguageProvider` + `LanguageLibrary.xml` (fr/en/zh) — bibliothèque embarquée (EmbeddedResource) surchargeable dans `~/.config/miflash/`, colonnes ENG/FRE/CHN_S(+CHN_T), repli langue→ENG→clé (l'original renvoyait vide), cache (l'original relisait le XML à chaque appel), `Detect()` depuis LC_ALL/LANG, option globale `--lang`, façade `L._()`
- [x] M4.7 `miflash doctor` : libusb, udev, permissions, fastboot présent, .NET OK — `DoctorService` : runtime (+ détection self-contained), contexte libusb + énumération, règles udev (51-miflash.rules, commande d'installation affichée), `fastboot` sur PATH (pré-requis M5), répertoires XDG créés ; WARN n'échoue pas, FAIL → exit 4
- [x] M4.8 Tests : **108 verts** (+16) — opérations standalone via `MultiDeviceFlasher` sur `ScriptedEdlDevice` (backup lit persist.img, restore écrit depuis `--from`, provision renvoie `<ufs>` sans `desc` + re-configure `SkipStorageInit="0"`, reboot partial_reset, reboot --edl reset_to_edl, backup sans XML échoue proprement), `LanguageProvider` (3 langues embarquées, repli CHN_T→ENG, clé inconnue, `Detect()`, surcharge XDG), `SessionLog` (fichier par appareil, pass/fail + result.log, Sanitize) ; publish self-contained linux-x64 testé (binaire `doctor`/`version` OK)

## M5 — Fastboot

- [ ] M5.1 `ProcessRunner` POSIX (port de `Cmd.cs` sans cmd.exe) : sortie ligne à ligne, timeout, kill arborescence
- [ ] M5.2 Détection fastboot : `fastboot devices` + corrélation libusb (serial ↔ bus/port)
- [ ] M5.3 Moteur de scripts natif : parsing `flash_all*.bat`/`.sh` en opérations (`fastboot flash -s …`), choix clean / except-storage / lock
- [ ] M5.4 Timeout global 11 min 40 s + kill (port `ScriptDevice.flash()`)
- [ ] M5.5 Cas Intel DNX : `fastboot boot loader.efi` (kernelflinger)
- [ ] M5.6 `miflash flash-fastboot <rom> [--script auto]`

## M6 — Packaging / GUI / durcissement

- [ ] M6.1 `dotnet publish` self-contained linux-x64 (+ arm64 si supporté), tarball
- [ ] M6.2 Paquets `.deb` / `.rpm`, AUR ; installation udev rules + reload
- [ ] M6.3 (option) GUI Avalonia 11 : liste appareils, progression, sélection répertoire
- [ ] M6.4 Docs utilisateur fr/en complètes + matrice matériel
- [ ] M6.5 Audit sécurité final (chemins, encodages, droits, clés) + tag `v1.0.0`

## Hors périmètre v1 (explicitement écartés — ne pas porter)

- MES/X5Mes (`X5MesLibrary.dll`), `iacj_interprocess_dll`, `PUB_TEST_FUNC_DLL`, `RoutingObject`
- Tegra `nvflash`, MediaTek `FlashToolLib`, JR/NSL `flash-loader`
- `ShareMemory` (IPC usine), OleDb/Excel, login compte Xiaomi (`Authentication.cs`), FTP usine (`FtpHelper`/`SwDownloader`)
- Installation de drivers Windows (`MiDriver`, `Driver`, setupapi) → remplacée par udev

## Blocages courants

| Symptôme | Piste |
|---|---|
| `error: only nop and sig tag…` | Auth NoAuth non déclenchée → vérifier `StaticBlobAuthenticator` (taille 256 o annoncée via `AUTHP`) |
| Pas de ACK après `<configure>` | Payload trop grand pour le loader (NAK 16384) → gérer comme `Comm.GetResponse` l. 374 |
| HELLO jamais reçu | Port 9008 non ouvert (permissions udev) ou interface non claimée → `miflash doctor` |
| Mauvais programmer | Fichier ne matchant pas `prog_.*_firehose_.*.elf` / DDR choisi → `--programmer` explicite |
