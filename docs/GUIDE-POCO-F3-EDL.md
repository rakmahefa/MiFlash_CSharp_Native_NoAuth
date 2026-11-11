# Guide de test — flash EDL d'un POCO F3 (alioth)

> **Petit guide pratique pour la première validation matérielle du portage
> (M2.7 / M3.10).** POCO F3 = `alioth` — Snapdragon 870 (SM8250-AC), stockage
> UFS. Usage éducatif : le flash EDL **efface tout l'appareil** et une erreur
> peut le bricker. Vous êtes seul responsable de l'opération.

## 1. Prérequis

- **miflash-linux v1.0.0+** installé ([README-linux § Installation](../README-linux.md)) :

  ```bash
  sudo apt install ./miflash-linux_1.0.0_amd64.deb   # ou tarball + udev à la main
  miflash doctor        # libusb OK, udev OK — tout doit être OK/WARN, aucun FAIL
  ```

- **ROM fastboot officielle** Xiaomi du POCO F3 (archive `..._images_...tgz`
  du modèle **alioth**/**alioth_global**, version égale ou plus récente que
  celle actuellement installée — ⚠️ ne **jamais** rétrograder en dessous du
  niveau anti-rollback (ARB), sous peine de brick définitif).
- **Programmer firehose** pour SM8250 : `prog_ufs_firehose_sm8250_ddr_4.elf`
  et/ou `prog_ufs_firehose_sm8250_ddr_5.elf` (le F3 est normalement en
  **ddr_5** ; si vous n'avez qu'un seul des deux et que le flash échoue au
  configure, essayez l'autre variante — idéal : mettez les **deux**, l'outil
  détecte la génération DDR tout seul).
- Câble **USB de données** (pas un câble de charge), port USB 2.0 de préférence.
- Batterie ≥ 50 %, ordinateur portable ou onduleur recommandé.

## 2. Préparer le répertoire ROM

Décompressez l'archive puis **placez le programmer dans le dossier `images/`**
(celui qui contient `rawprogram*.xml` et `patch*.xml`) :

```
alioth_global_images_V14.0.x.x/
├── flash_all.bat / flash_all.sh          ← pour la méthode fastboot (§7)
└── images/
    ├── rawprogram0.xml … patch4.xml      ← déjà inclus (ROM officielle)
    ├── boot.img, system.img, xbl.img …   ← déjà inclus
    └── prog_ufs_firehose_sm8250_ddr_5.elf  ← À COPIER ICI (votre programmer)
```

L'outil descend automatiquement dans le sous-dossier `images/` s'il existe :
vous pouvez donc passer soit la racine ROM, soit directement `images/`.
Vérifiez que le dossier contient bien `rawprogram*.xml` + `patch*.xml`
(inclus dans les ROMs fastboot officielles Xiaomi).

## 3. Passer le POCO F3 en mode EDL (9008)

Trois méthodes, de la plus simple à la plus radicale :

1. **Le téléphone démarre encore (système ou recovery)** :

   ```bash
   adb reboot edl
   ```

2. **Le téléphone démarre en fastboot** : `fastboot reboot recovery` puis,
   depuis le recovery, `adb reboot edl` — ou directement la méthode 3.

3. **Le téléphone est éteint (toujours possible si le bootloader amorce)** :
   téléphone **éteint**, maintenez **Volume –** appuyé et **branchez le câble
   USB** (maintenir ~5–10 s). L'appareil s'allume directement en mode 9008
   (écran noir — c'est normal).

4. **En dernier recours (brick complet)** : point de test matériel alioth
   (démontage de la coque arrière, court-circuit du point de test avec la
   masse pendant le branchement) — voir un guide alioth dédié pour
   l'emplacement exact.

## 4. Vérifier la détection

```bash
miflash devices
```

Le POCO F3 doit apparaître comme **EDL 9008** avec `05c6:9008` (colonne
usb) et une localisation `bus.port` (ex. `1.3`). Sinon :

```bash
miflash doctor    # udev installé ? permissions ?
```

## 5. Flasher

```bash
miflash flash /chemin/vers/alioth_global_images_V14.0.x.x
```

- L'outil affiche l'avertissement éducatif, liste la cible (`9008@1.3`) et
  demande la confirmation : tapez **`yes`** (ou passez `--yes`).
- `--programmer auto` est le défaut : il prend le programmer firehose présent
  dans le dossier ; si les **deux** variantes DDR y figurent, il interroge la
  cible (peek CPUID) et recharge automatiquement le bon.
- Déroulé : Sahara (envoi du programmer) → configure DDR → provision UFS si
  `provision*.xml` → rawprogram (écriture des partitions) → patch →
  **redémarrage automatique** en fin de flash (PartialReset).
- Progression par appareil affichée en direct ; durées typiques : 5–15 min.

## 6. Suivi et logs

Chaque appareil a son journal horodaté, rangé après l'opération :

```
~/.local/state/miflash/log/<yyyyMd>/pass|fail/9008@<date>.txt
~/.local/state/miflash/log/result.log          ← verdict global
```

**Après un flash réussi** : le téléphone redémarre tout seul. Le premier
démarrage peut prendre plusieurs minutes (optimisation des apps) — ne coupez
rien. Mise à jour des applis/Google Play ensuite.

## 7. Plan B — flash fastboot (si le téléphone démarre encore)

Si le bootloader est accessible (`fastboot` à l'écran, Vol– + Vol+ à
l'allumage), la méthode la plus douce utilise votre ROM fastboot sans EDL :

```bash
sudo apt install fastboot          # si absent (cf. miflash doctor)
miflash flash-fastboot /chemin/vers/alioth_global_images_V14.0.x.x
```

Le moteur natif parse `flash_all*.sh|.bat` (aucun script externe exécuté).
`--script except-storage` évite d'effacer le stockage/les données.

## 8. En cas d'échec

| Symptôme | Piste |
|---|---|
| `can not found programmer file` | le `.elf` n'est pas dans le dossier `images/` (la recherche est **non récursive**) |
| Pas de ACK après `<configure>` / échec DDR | mauvaise génération DDR → mettez `prog_ufs_firehose_sm8250_ddr_4.elf` **et** `_ddr_5.elf` dans le dossier, relancez (l'auto-détection tranche) |
| `HELLO` jamais reçu | appareil sorti du 9008, câble de charge, permissions udev → `miflash doctor`, rebrancher |
| Échec en cours de flash | ne pas débrancher : l'appareil reste en 9008, relancer `miflash flash` suffit |
| Téléphone en bootloop après | premier démarrage long (10 min max) ; sinon reflash, ou version ROM plus récente (ARB) |

**Signalez le résultat** (réussite comme échec) : ouvrez une issue GitHub avec
le modèle exact, la sortie de `miflash doctor`, le log
`~/.local/state/miflash/log/…/9008@….txt` et, si possible, un flash relancé
avec `--read-dump --write-dump` (trames hexadécimales) — cela alimentera la
matrice matérielle (M3.10). Merci !

---

*Outil fourni à des fins éducatives, sans affiliation Xiaomi/Qualcomm.
Les loaders firehose ne sont pas distribués avec l'outil.*
