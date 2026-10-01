FERRARI POS V31 · CORRECCIÓN PROFUNDA CLOUDFLARE + LOGIN
==========================================================

1. Se corrigió el problema grave de captura de URL de Cloudflare.
   - cloudflared puede mostrar https://api.trycloudflare.com/tunnel durante el alta.
   - FerrariPOS lo confundía con el hostname público porque la expresión anterior
     aceptaba cualquier subdominio de trycloudflare.com.
   - Ahora api.trycloudflare.com queda explícitamente bloqueado.
   - Solo se acepta un hostname público real del tipo https://<id>.trycloudflare.com/.
   - También se rechazan rutas como /tunnel y otros endpoints internos.

2. Se agregó recuperación automática real.
   - Si el Quick Tunnel queda sin responder durante 90 segundos continuos, se reinicia
     cloudflared de forma controlada y se obtiene un nuevo enlace.
   - Al terminar un proceso se elimina el enlace público viejo de AccessUrl y se muestra
     temporalmente la dirección LAN mientras se recupera el túnel.
   - WaitForPublicUrlAsync y GetMobileBaseUrl ya no aceptan una URL inválida.

3. Se amplió la regla de firewall local del instalador.
   - FerrariPOS puede elegir 8787..8806 si el puerto preferido está ocupado.
   - La regla privada cubre todo ese rango.
   - No se abre el servidor directamente a Internet; el acceso público continúa siendo
     mediante la conexión saliente de cloudflared.

4. Login Windows.
   - Se mantiene la lógica, usuarios, licencia y botones.
   - El logo exacto elegido por el usuario ocupa prácticamente todo el panel izquierdo.
   - Se creó un poster vertical a partir del logo exacto, preservando sus proporciones.
   - Panel derecho actualizado a estética futurista oscura con acentos cian/dorado.
   - No se cambiaron acciones de los botones.

5. No se modificaron Android Manager, sonidos MP3, QR/Cloudflare pairing, base de datos,
   licencias offline ni lógica de ventas, salvo las validaciones de URL necesarias para
   impedir que una URL interna de Cloudflare se use como conexión.

IMPORTANTE SOBRE QUICK TUNNEL
-----------------------------
Quick Tunnel genera un hostname aleatorio y temporal. Por diseño, un reinicio que cree
un túnel nuevo puede producir otra URL pública. V31 evita el fallo grave de confundir el
endpoint API con el túnel y recupera automáticamente el servicio, pero una URL pública
permanente después de reinicios requiere un Cloudflare Tunnel administrado con hostname
fijo. El fallback LAN existente continúa disponible cuando Android y Windows están en
la misma red.
