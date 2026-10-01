# FerrariPOS - Configuración automática de red y Cloudflare
# No depende del nombre del adaptador (WiFi, WiFi 2, Ethernet, etc.).
$ErrorActionPreference = 'Continue'
$logDir = Join-Path $env:ProgramData "FerrariPOS"
$log = Join-Path $logDir "setup_red_cloudflare.log"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
function Log($m) { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $m" | Out-File -FilePath $log -Append -Encoding utf8 }
Log "INICIO configuración automática de red."

# Solo interfaces IPv4 activas con puerta de enlace. Se identifican por InterfaceIndex,
# nunca por el nombre visible del adaptador.
try {
    $configs = Get-NetIPConfiguration -ErrorAction Stop | Where-Object {
        $_.NetAdapter.Status -eq 'Up' -and
        $_.IPv4Address -and
        $_.IPv4DefaultGateway
    }
} catch {
    Log "Get-NetIPConfiguration no disponible: $($_.Exception.Message)"
    $configs = @()
}

foreach ($cfg in $configs) {
    $idx = $cfg.InterfaceIndex
    $alias = $cfg.InterfaceAlias
    try {
        Log "INTERFAZ ACTIVA: índice=$idx nombre='$alias' IPv4=$($cfg.IPv4Address.IPAddress) gateway=$($cfg.IPv4DefaultGateway.NextHop)"
        # DNS estable para el acceso a Cloudflare. No se depende del nombre del adaptador.
        Set-DnsClientServerAddress -InterfaceIndex $idx -ServerAddresses @('1.1.1.1','1.0.0.1','8.8.8.8','8.8.4.4') -ErrorAction Stop
        Log "DNS CONFIGURADO: índice=$idx -> 1.1.1.1, 1.0.0.1, 8.8.8.8, 8.8.4.4"
    } catch {
        Log "ERROR DNS índice=$idx nombre='$alias': $($_.Exception.Message)"
    }
}

try { ipconfig.exe /flushdns | Out-Null; Log "DNS CACHE: flush realizado." } catch { Log "ERROR flushdns: $($_.Exception.Message)" }

# Dejar preparado el firewall para el servidor local únicamente. No se abre el puerto
# hacia Internet: Cloudflare crea la salida desde el equipo hacia su red.
# FerrariPOS puede desplazarse desde 8787 hasta 8806 si otro proceso ocupa el puerto
# preferido; por eso se habilita el rango privado completo que el servidor puede usar.
try {
    $ruleName = 'FerrariPOS servidor local 8787-8806'
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8787-8806 -Profile Private -ErrorAction Stop | Out-Null
        Log "FIREWALL: regla privada TCP 8787-8806 creada."
    } else { Log "FIREWALL: regla TCP 8787-8806 ya existe." }
} catch { Log "FIREWALL: no se pudo crear regla (no es crítico): $($_.Exception.Message)" }

# Cloudflare Tunnel usa conexiones SALIENTES por TCP/UDP 7844.
# No se abre ningún puerto entrante de Internet para Cloudflare. Estas reglas
# solo aseguran que un firewall de Windows administrado de forma restrictiva
# permita la salida necesaria del túnel.
try {
    foreach ($proto in @('TCP','UDP')) {
        $ruleName = "FerrariPOS Cloudflare salida 7844 $proto"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Outbound -Action Allow -Protocol $proto -RemotePort 7844 -Profile Any -ErrorAction Stop | Out-Null
            Log "FIREWALL: regla saliente $proto 7844 creada."
        } else { Log "FIREWALL: regla saliente $proto 7844 ya existe." }
    }
} catch { Log "FIREWALL: no se pudo crear regla saliente 7844 (Windows normalmente permite salida por defecto): $($_.Exception.Message)" }

Log "FIN configuración automática de red."
