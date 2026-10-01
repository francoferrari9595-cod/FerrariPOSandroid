V16.6 - Corrección profunda de sincronización QR / Cloudflare

- Corrige el health-check de Windows: /api/mobile/ping ahora se consulta con X-FerrariPOS-Token. Antes el health-check recibía 401 aunque el túnel estuviera operativo y lo marcaba como RECONECTANDO.
- Windows ya no reemplaza la URL pública por LAN en cada intento de reconexión; conserva la última URL pública mientras cloudflared sigue vivo.
- Si cloudflared realmente termina, el reinicio del conector usa un backoff corto para recuperar el servicio sin ciclos agresivos.
- Android incorpora ConnectionRoute: mantiene una ruta activa y cambia de forma persistente a la LAN cuando una lectura recibe 530/502/503/504 o un fallo de red; también puede volver a la pública cuando la LAN falla. Esto aplica a todas las solicitudes, y las escrituras NO se repiten.
- El QR de Windows ahora contiene directamente el payload completo (URL pública + URL LAN + token), evitando una segunda petición pública durante la vinculación.
- Se mantienen los efectos visuales, reportes, clientes y demás cambios de V16.5.
- Nota: Quick Tunnel/trycloudflare es una función de prueba de Cloudflare. Para disponibilidad pública permanente se recomienda un túnel administrado con hostname fijo.
