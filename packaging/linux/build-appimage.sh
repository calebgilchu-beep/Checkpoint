#!/usr/bin/env bash
# Crea Checkpoint-<versión>-<arch>.AppImage. Uso: packaging/linux/build-appimage.sh <x86_64|aarch64> [salida]
# Se ejecuta en Linux (lo hace .github/workflows/linux-appimage.yml en un runner de cada arquitectura).
set -euo pipefail

ARCH="${1:?arquitectura: x86_64 o aarch64}"
OUT="${2:-dist}"
case "$ARCH" in
  x86_64)  RID=linux-x64 ;;
  aarch64) RID=linux-arm64 ;;
  *) echo "Arquitectura no válida: $ARCH" >&2; exit 2 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Checkpoint.Linux/Checkpoint.Linux.csproj" | head -1)"
WORK="$(mktemp -d)"
APPDIR="$WORK/Checkpoint.AppDir"
trap 'rm -rf "$WORK"' EXIT

dotnet publish "$ROOT/src/Checkpoint.Linux" -c Release -r "$RID" -o "$WORK/pub"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp "$WORK/pub/Checkpoint" "$APPDIR/usr/bin/"
# Photino.Native.so tiene que estar junto al ejecutable (no se puede extraer de un single-file).
cp "$WORK/pub/"*.so "$APPDIR/usr/bin/" 2>/dev/null || true
chmod +x "$APPDIR/usr/bin/Checkpoint"
install -m 755 "$ROOT/packaging/linux/AppRun" "$APPDIR/AppRun"
cp "$ROOT/packaging/linux/checkpoint.desktop" "$APPDIR/checkpoint.desktop"
cp "$ROOT/packaging/linux/checkpoint.desktop" "$APPDIR/usr/share/applications/"
cp "$ROOT/packaging/linux/checkpoint.png" "$APPDIR/checkpoint.png"
cp "$ROOT/packaging/linux/checkpoint.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/"
ln -s checkpoint.png "$APPDIR/.DirIcon"

TOOL="$WORK/appimagetool"
curl -fsSL -o "$TOOL" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"
chmod +x "$TOOL"
mkdir -p "$OUT"
FILE="$OUT/Checkpoint-$VERSION-$ARCH.AppImage"
ARCH="$ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$FILE"
echo "$FILE"
