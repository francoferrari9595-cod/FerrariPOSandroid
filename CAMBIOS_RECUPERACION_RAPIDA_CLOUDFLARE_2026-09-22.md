# FerrariPOS — recuperación rápida de conexión Cloudflare

## Alcance
Se modificó únicamente la capa de recuperación/diagnóstico de conexión de Windows.
No se modificaron archivos de Android ni componentes visuales de Android o Windows.

## Cambios
- El monitoreo de salud del enlace público pasa de 15 s a 3 s para detectar rápidamente una recuperación.
- Si `cloudflared` termina, el siguiente intento se realiza con una espera de 1 s.
- Si el ciclo necesita repetir una comprobación, usa una espera corta de 2 s en lugar del backoff largo anterior.
- Se mantiene `--protocol auto` y `--edge-ip-version 4`, permitiendo que cloudflared negocie QUIC/UDP o HTTP/2/TCP según la red disponible.
- La recuperación de red ya no cambia automáticamente los servidores DNS del cliente; solamente limpia la caché DNS y vuelve a comprobar conectividad. Esto evita alterar la configuración de red del cliente.
- `REPARAR CONEXIÓN` conserva la reparación automática.
- `GENERAR DIAGNÓSTICO` ahora genera el informe sin ejecutar acciones de reparación.
- Se conserva el informe en el Escritorio: `FerrariPOS_Diagnostico.txt`.

## Nota de validación
No se pudo ejecutar `dotnet build` en este entorno porque el SDK de .NET no está disponible. Se realizó comprobación estructural de los archivos modificados y el ZIP final se verificó con `unzip -t`.
