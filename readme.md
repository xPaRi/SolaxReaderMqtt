# SolaxReaderMqtt
### Funkce
Čte data ze střídače Solax, malinko je přežvýká a pošle je na MQTT Brooker

### Příprava RPi
1. Přihlášení certifikátem (viz. `c:\Users\pari\.ssh\`)
    **`config`**
    ```
    Host rpi Raspberry na pokusy s .NET
    HostName rpi2b
    IdentityFile ~/.ssh/rpi2b
    User pi
    ```
    **`rpi2b`**
    Soubor s privátním klíčem

    **`rpi2b.pub`**
    Soubor s veřejným klíčem

2. Instalace .NETu

    ```bash
    sudo apt update
    sudo apt install -y libicu-dev zlib1g libssl-dev
    sudo apt install -y aspnetcore-runtime-8.0
    ```


### Nasazení
Jelikož nejsem Linux Guru, tak to tu popíšu krok za krokem:

Vytvoříme službu **solax.service**

Toto vyvolá editor:`sudo nano /etc/systemd/system/solax.service`

Vložíme:
```ini
[Unit]
Description=Solax Data Reader MQTT Service
After=network.target

[Service]
# Tady nastav proměnné prostředí, které tvůj Program.cs vyžaduje
Environment="SOLAX_URI=192.168.1.52"
Environment="MQTT_BROKER_URI=192.168.1.1"
Environment="SOLAX_READER_DELAY=5"

# Cesta k tvému zkompilovanému souboru
ExecStart=/home/pi/sr/SolaxReaderMqtt
WorkingDirectory=/home/pi/sr
StandardOutput=inherit
StandardError=inherit
User=pi
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Aktivujeme službu

```bash
sudo systemctl daemon-reload
sudo systemctl enable solax.service
sudo systemctl start solax.service
```

### Kompilace zdrojáku, jeho doručení a restart služby

Spustit povelový soubor:
`c:\dev\PARI\SolaxReaderMqtt\public.cmd`

### Ostatní
Velikost složek v aktuálním adresáři: `du -sh *`
Celková RAM: `free -h`
Stav SolaxReaderu: `sudo systemctl status solax.service`
Výpis z běžící service: `journalctl -u solax.service -f`

