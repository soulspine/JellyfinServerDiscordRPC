#!/bin/bash

# ------ CONFIG ------
SERVER_ALIAS="localhost"
REMOTE_PLUGIN_DIR="/home/soulspine/.var/app/org.jellyfin.JellyfinServer/data/jellyfin/plugins/DiscordRPC"
DLL_NAME="DiscordRpcPlugin.dll"
LOCAL_DLL_PATH="bin/Debug/net9.0/$DLL_NAME"
PROJ_FILE="DiscordRpcPlugin.csproj"
# --------------------

dotnet build "$PROJ_FILE"

# Sprawdź czy build się udał
if [ $? -ne 0 ]; then
    echo "Build failed. Aborting."
    exit 1
fi

echo "Build completed. Transferring plugin to server..."

if [ "$SERVER_ALIAS" == "localhost" ]; then
    flatpak kill org.jellyfin.JellyfinServer
    cp "$LOCAL_DLL_PATH" "$REMOTE_PLUGIN_DIR/"
    setsid flatpak run org.jellyfin.JellyfinServer >/dev/null 2>&1 < /dev/null &
    echo "File copied locally."
    echo "Done!"
    exit 0
fi

# Kopiowanie pliku
scp "$LOCAL_DLL_PATH" "$SERVER_ALIAS:$REMOTE_PLUGIN_DIR/"

if [ $? -eq 0 ]; then
    echo "File transferred correctly."
    
    # Zdalne wykonanie komend przez SSH
    ssh -t "$SERVER_ALIAS" "chown jellyfin:jellyfin $REMOTE_PLUGIN_DIR/$DLL_NAME && chmod 644 $REMOTE_PLUGIN_DIR/$DLL_NAME && systemctl restart jellyfin"
    
    echo "Done!"
else
    echo "Error during file transfer."
    exit 1
fi