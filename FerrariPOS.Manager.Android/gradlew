#!/bin/sh
set -e
APP_HOME=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GRADLE_VERSION=8.11.1
GRADLE_USER_DIR="${GRADLE_USER_HOME:-$HOME/.gradle}"
DIST_DIR="$GRADLE_USER_DIR/ferrari-gradle"
GRADLE_HOME="$DIST_DIR/gradle-$GRADLE_VERSION"

if [ ! -x "$GRADLE_HOME/bin/gradle" ]; then
  mkdir -p "$DIST_DIR"
  ZIP="$DIST_DIR/gradle-$GRADLE_VERSION-bin.zip"
  URL="https://services.gradle.org/distributions/gradle-$GRADLE_VERSION-bin.zip"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$URL" -o "$ZIP"
  elif command -v wget >/dev/null 2>&1; then
    wget -q "$URL" -O "$ZIP"
  else
    echo "ERROR: se necesita curl o wget para descargar Gradle $GRADLE_VERSION." >&2
    exit 1
  fi
  rm -rf "$GRADLE_HOME"
  if command -v unzip >/dev/null 2>&1; then
    unzip -q "$ZIP" -d "$DIST_DIR"
  else
    echo "ERROR: se necesita unzip para instalar Gradle." >&2
    exit 1
  fi
fi

exec "$GRADLE_HOME/bin/gradle" "$@"
