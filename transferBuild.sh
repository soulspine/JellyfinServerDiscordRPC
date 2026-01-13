#!/bin/bash

# THIS IS JUST A HELPER SCRIPT FOR ME SPECIFICALLY

# ------ CONFIG ------
SERVER_ALIAS="jellyfin"
REMOTE_PLUGIN_DIR="/home/soulspine/.var/app/org.jellyfin.JellyfinServer/data/jellyfin/plugins/DiscordRPC"
# Zmieniamy ścieżkę na folder, nie na konkretny plik
LOCAL_BIN_PATH="bin/Release/net9.0"
PROJ_FILE="DiscordRpcPlugin.csproj"
# --------------------

if [ "$SERVER_ALIAS" != "localhost" ]; then
    REMOTE_PLUGIN_DIR="/var/lib/jellyfin/plugins/DiscordRPC"
fi
dotnet build -c Release "$PROJ_FILE"

if [ $? -ne 0 ]; then
    echo "Build failed. Aborting."
    exit 1
fi

echo "Build completed. Transferring files to server..."

mkdir -p "$REMOTE_PLUGIN_DIR"

if [ "$SERVER_ALIAS" == "localhost" ]; then
    flatpak kill org.jellyfin.JellyfinServer
    
    rm -rf "$REMOTE_PLUGIN_DIR"
    mkdir -p "$REMOTE_PLUGIN_DIR"

    cp "$LOCAL_BIN_PATH"/*.dll "$REMOTE_PLUGIN_DIR/"
    
    echo "All DLLs copied locally to $REMOTE_PLUGIN_DIR"
    flatpak run org.jellyfin.JellyfinServer &
    echo "Done!"
    exit 0
fi

scp "$LOCAL_BIN_PATH"/*.dll "$SERVER_ALIAS:$REMOTE_PLUGIN_DIR/"

if [ $? -eq 0 ]; then
    echo "Files transferred correctly."
    ssh -t "$SERVER_ALIAS" "chown -R jellyfin:jellyfin $REMOTE_PLUGIN_DIR/ && chmod 644 $REMOTE_PLUGIN_DIR/*.dll && systemctl restart jellyfin"
    echo "Done!"
else
    echo "Error during file transfer."
    exit 1
fi