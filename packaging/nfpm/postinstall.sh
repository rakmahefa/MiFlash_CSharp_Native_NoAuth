#!/bin/sh
# MiFlash Linux — post-installation (.deb postinst / .rpm %post)
# Recharge les règles udev et signale les étapes suivantes.
set -e

if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules 2>/dev/null || true
    udevadm trigger --action=add 2>/dev/null || true
fi

echo ""
echo "miflash-linux installé."
echo "  1. Vérifiez votre environnement : miflash doctor"
echo "  2. (re)branchez l'appareil pour appliquer les règles udev"
echo "  3. Aide : miflash --help"
echo ""
