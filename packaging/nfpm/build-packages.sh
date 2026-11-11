#!/usr/bin/env bash
# MiFlash Linux — construction des paquets .deb/.rpm via nfpm.
#
# Usage :
#   packaging/nfpm/build-packages.sh <version> <dir-x64> [dir-arm64]
#
#   <version>   sans le « v » (ex. 1.0.0)
#   <dir-x64>   sortie « dotnet publish -r linux-x64 --self-contained »
#   [dir-arm64] idem linux-arm64 (facultatif)
#
# Prérequis : nfpm dans le PATH (https://nfpm.goreleaser.com/).
# Sortie (répertoire courant) :
#   miflash-linux_<version>_amd64.deb
#   miflash-linux_<version>_arm64.deb
#   miflash-linux-<version>.x86_64.rpm
#   miflash-linux-<version>.aarch64.rpm
set -euo pipefail

VERSION="${1:?usage: build-packages.sh <version> <dir-x64> [dir-arm64]}"
DIR_X64="${2:?usage: build-packages.sh <version> <dir-x64> [dir-arm64]}"
DIR_ARM64="${3:-}"

outdir="$(pwd)"   # sortie des paquets = répertoire d'appel (pas la racine du dépôt)

root="$(cd "$(dirname "$0")/../.." && pwd)"   # racine du dépôt
template="$root/packaging/nfpm/nfpm.yaml"

command -v nfpm >/dev/null || { echo "nfpm introuvable dans le PATH" >&2; exit 1; }

# Vérifie que chaque répertoire contient bien le binaire « miflash ».
for d in "$DIR_X64" $DIR_ARM64; do
    [ -x "$d/miflash" ] || { echo "binaire $d/miflash introuvable" >&2; exit 1; }
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Table de correspondance d'architectures (publish → deb/rpm).
build() { # <dir> <arch_deb> <arch_rpm>
    local dir="$1" arch_deb="$2" arch_rpm="$3" cfg format arch licdst

    for format in deb rpm; do
        arch="$arch_deb"
        licdst="/usr/share/doc/miflash-linux/copyright"        # convention Debian
        target="${outdir}/miflash-linux_${VERSION}_${arch}.deb"  # name_version_arch.deb
        if [ "$format" = "rpm" ]; then
            arch="$arch_rpm"
            licdst="/usr/share/licenses/miflash-linux/LICENSE"  # convention RPM
            target="${outdir}/miflash-linux-${VERSION}.${arch}.rpm"  # name-version.arch.rpm
        fi
        cfg="$tmp/nfpm-${arch}-${format}.yaml"

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

echo "Paquets générés :"
ls -la "${outdir}"/miflash-linux_*.deb "${outdir}"/miflash-linux-*.rpm 2>/dev/null
