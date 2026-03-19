@echo off
set RPI_TARGET=rpi
set DEST_DIR=/home/pi/sr
set APP_NAME=SolaxReaderMqtt

echo --- 1. Kompilace a publikace ---
dotnet publish .\SolaxReaderMqtt.csproj -r linux-arm -c Release --self-contained false -p:PublishSingleFile=true -p:InvariantGlobalization=true -p:PublishTrimmed=true -p:TrimMode=partial

echo --- 2. Zastaveni sluzby (Povoleni zamku) ---
ssh %RPI_TARGET% "sudo systemctl stop solax.service"

echo --- 3. Kopirovani na RPi ---
scp "bin\Release\net8.0\linux-arm\publish\%APP_NAME%" %RPI_TARGET%:%DEST_DIR%/

echo --- 4. Restart a Kontrola ---
ssh %RPI_TARGET% "sudo systemctl restart solax.service && journalctl -u solax.service -f -o short-iso"
