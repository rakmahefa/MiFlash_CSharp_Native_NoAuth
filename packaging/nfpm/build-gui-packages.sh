#!/usr/bin/env bash
# MiFlash Linux GUI — construction des paquets .deb/.rpm via nfpm.
#
# Usage :
#   packaging/nfpm/build-gui-packages.sh <version> <gui-dir-x64> [gui-dir-arm64]
#
#   <version>    sans le « v » (ex. 1.0.1)
#   <gui-dir-x64>  sortie « dotnet publish src/MiFlash.Gui -r linux-x64
#                  --self-contained »
#   [gui-dir-arm64] idem linux-arm64 (facultatif)
#
# Prérequis : nfpm dans le PATH (https://nfpm.goreleaser.com/).
# Sortie (répertoire courant) :
#   miflash-linux-gui_<version>_amd64.deb
#   miflash-linux-gui_<version>_arm64.deb
#   miflash-linux-gui-<version>.x86_64.rpm
#   miflash-linux-gui-<version>.aarch64.rpm
set -euo pipefail

VERSION="${1:?usage: build-gui-packages.sh <version> <gui-dir-x64> [gui-dir-arm64]}"
DIR_X64="${2:?usage: build-gui-packages.sh <version> <gui-dir-x64> [gui-dir-arm64]}"
DIR_ARM64="${3:-}"

outdir="$(pwd)"   # sortie des paquets = répertoire d'appel (pas la racine du dépôt)

root="$(cd "$(dirname "$0")/../.." && pwd)"   # racine du dépôt
template="$root/packaging/nfpm/nfpm-gui.yaml"

command -v nfpm >/dev/null || { echo "nfpm introuvable dans le PATH" >&2; exit 1; }

# Le guide est embarqué dans le paquet (/usr/share/doc) — vérifier sa présence.
[ -f "$root/docs/GUIDE-GUI.md" ] || { echo "docs/GUIDE-GUI.md introuvable" >&2; exit 1; }

# Vérifie que chaque répertoire contient bien le binaire « miflash-gui ».
for d in "$DIR_X64" $DIR_ARM64; do
    [ -x "$d/miflash-gui" ] || { echo "binaire $d/miflash-gui introuvable" >&2; exit 1; }
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Table de correspondance d'architectures (publish → deb/rpm).
build() { # <dir> <arch_deb> <arch_rpm>
    local dir="$1" arch_deb="$2" arch_rpm="$3" cfg format arch licdst

    for format in deb rpm; do
        arch="$arch_deb"
        licdst="/usr/share/doc/miflash-linux-gui/copyright"          # convention Debian
        target="${outdir}/miflash-linux-gui_${VERSION}_${arch}.deb"  # name_version_arch.deb
        if [ "$format" = "rpm" ]; then
            arch="$arch_rpm"
            licdst="/usr/share/licenses/miflash-linux-gui/LICENSE"   # convention RPM
            target="${outdir}/miflash-linux-gui-${VERSION}.${arch}.rpm"  # name-version.arch.rpm
        fi
        cfg="$tmp/nfpm-gui-${arch}-${format}.yaml"

        sed -e "s|@@VERSION@@|${VERSION}|g" \
            -e "s|@@ARCH@@|${arch}|g" \
            -e "s|@@DIR@@|${dir}|g" \
            -e "s|@@LICDST@@|${licdst}|g" \
            "$template" > "$cfg"

        (cd "$root" && nfpm package -f "$cfg" -p "$format" -t "$target")
    done
}

build "$DIR_X64" amd64 x86_64
[ -n "$DIR_ARM64" ] && build "$DIR_ARM64" arm64 aarch64

echo "Paquets GUI générés :"
ls -la "${outdir}"/miflash-linux-gui_*.deb "${outdir}"/miflash-linux-gui-*.rpm 2>/dev/null
