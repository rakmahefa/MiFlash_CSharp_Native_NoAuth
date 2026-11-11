#!/bin/sh
# MiFlash Linux GUI — post-installation (.deb postinst / .rpm %post)
# Recharge les caches d'icônes/menus (best effort — headless tolerant).
set -e

# Bases de données .desktop (facultatif hors environnement de bureau).
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications 2>/dev/null || true
fi

# Cache d'icônes hicolor (l'icône SVG est utilisable sans, mais le cache
# accélère les premiers affichages).
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
fi

echo ""
echo "miflash-linux-gui installé."
echo "  1. Lancez « miflash-gui » depuis le menu (catégorie Système)"
echo "     ou en terminal : miflash-gui"
echo "  2. Vérifiez d'abord l'environnement : miflash doctor"
echo "  3. Guide complet : /usr/share/doc/miflash-linux-gui/GUIDE-GUI.md"
echo "     (en ligne : docs/GUIDE-GUI.md du dépôt)"
echo ""
