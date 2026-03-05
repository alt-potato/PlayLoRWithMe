#!/usr/bin/env bash
set -euo pipefail

#
# Builds and installs the mod into the game directory, then launches the game.
# Directory configurations are pulled from the .env file.
#

MOD_NAME=PlayLoRWithMe
LOR_ID=1256670

# pull env vars
source .env

if [ -z "$BUILD_DIR" ]; then
    BUILD_DIR="$(pwd)"
fi
if [ -z "$MOD_DIR" ]; then
    echo "MOD_DIR is not set, nowhere to install the mod."
    exit 1
fi

# build the mod
echo "> building mod..."

cd "$BUILD_DIR"
dotnet build # debug build
# dotnet publish # release build

# install the mod
echo "> installing mod..."

cd "$MOD_DIR"
rm -rf "$MOD_NAME"
cp -r "$BUILD_DIR/mod/bin/Debug/$MOD_NAME" .

# open the game
echo "> launching game (with mods)..."

# try linux and windows (git bash)
( steam steam://rungameid/$LOR_ID -mod 2> /dev/null ) || ( "/c/Program Files (x86)/Steam/steam.exe" -applaunch $LOR_ID -mod )

echo "> done!"
