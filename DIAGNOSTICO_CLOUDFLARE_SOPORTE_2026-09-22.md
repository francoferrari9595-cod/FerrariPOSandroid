# FerrariPOS — Diagnóstico y reparación de conexión Cloudflare

Se agregó una capa de soporte en Windows sin reemplazar la conexión Cloudflare existente.

## F7 → Configuración

- **REPARAR CONEXIÓN**: comprueba Internet, DNS, servidor local, cloudflared, conectividad Cloudflare y URL pública; intenta recuperación controlada y deja el informe en el Escritorio.
- **GENERAR DIAGNÓSTICO**: genera el mismo informe técnico para soporte.

El archivo siempre se guarda con un nombre sencillo:

`Escritorio\FerrariPOS_Diagnostico.txt`

## Qué informa

- Internet y DNS de los endpoints de Cloudflare.
- Servidor local y puerto seleccionado por FerrariPOS.
- Presencia/versión de cloudflared.
- TCP 7844 hacia las regiones de Cloudflare.
- Acceso a la API de Cloudflare por HTTPS.
- `cloudflared tunnel diag` cuando la versión instalada lo admite, incluyendo las comprobaciones nativas de UDP/TCP 7844.
- URL pública y respuesta de la API móvil autenticada.
- Reparaciones realizadas y resultado final.

## Compatibilidad de red

El Quick Tunnel usa selección automática de protocolo para permitir que cloudflared utilice QUIC/UDP o HTTP/2/TCP según lo que permita la red. El puerto 7844 es de salida; no se abre un puerto entrante de Internet para Cloudflare.

El instalador también prepara reglas de salida TCP/UDP 7844 en Windows Firewall cuando Windows permite crearlas.
