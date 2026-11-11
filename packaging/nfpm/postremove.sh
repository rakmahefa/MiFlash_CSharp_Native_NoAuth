#!/bin/sh
# MiFlash Linux — post-suppression (.deb postrm / .rpm %postun)
# Recharge udev sans les règles retirées (les fichiers du paquet sont déjà
# supprimés à ce stade pour « remove », conservés pour « upgrade »).
set -e

if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules 2>/dev/null || true
fi
