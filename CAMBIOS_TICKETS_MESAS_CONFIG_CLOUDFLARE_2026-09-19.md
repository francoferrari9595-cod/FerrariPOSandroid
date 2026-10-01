# FerrariPOS · Corrección profunda 2026-09-19

## Mesas 3D
- Se recuperó la mesa 3D directamente dentro del proyecto, sin depender de una imagen fija.
- `FerrariPOS.Windows/Forms/NeonTableControl.cs` dibuja la mesa con GDI+: profundidad/extrusión, sombra suave, iluminación cian y variante roja para ocupada/reservada.
- No se dibuja marco rectangular exterior.
- La mesa responde al ancho/alto guardado y puede estirarse/alargarse.
- Las sillas se calculan automáticamente según la capacidad configurada de la mesa.
- Se conservaron además los PNG 3D históricos en `Resources/tables3d/` para no perder los recursos anteriores.

## Tickets Windows → Android
- Android ahora recibe **todas las ventas abiertas que están actualmente en las pestañas de Windows**, incluyendo tickets normales y tickets asociados a mesas.
- Se eliminó el filtro que ocultaba tickets provenientes de flujos móviles.
- Cada ticket expuesto usa el identificador remoto estable `ticketRemoteIds`, evitando que eliminar una pestaña cambie el ticket que Android intenta modificar/cobrar.
- Android puede ver líneas, cantidades, precios y total.
- Si ADMIN habilita la opción de Windows, Android puede agregar productos y cobrar el ticket.
- El servidor vuelve a validar `allow_android_charge` en cada modificación/cobro.
- El cobro elimina el ticket abierto de Windows y registra la venta normal en la base de datos, conservando la mesa cuando corresponde.
- Se reemplazó el estado mágico `__PAY__` por estado Compose explícito para abrir el diálogo de cobro.

## Configuración Windows
- Se corrigieron superposiciones visibles en F7:
  - Link POS / Cloudflare ya no queda debajo de Identidad del comercio.
  - Idioma e impresión ocupan su fila sin superponerse con Identidad del ticket.
  - Apariencia/tipografía queda separada de Identidad del ticket.
  - Sonido, correo, segundo correo, resolución, WhatsApp, mesas, inventario y módulos quedaron desplazados respetando el orden vertical y el AutoScroll.

## Cloudflare / servidor público / instalador
- El Setup de Windows vuelve a incluir `cloudflared.exe` dentro de `publish` antes de generar el instalador.
- El workflow de GitHub descarga y valida el ejecutable oficial de cloudflared antes de construir el Setup.
- El instalador sigue ejecutando `Setup_Network_Cloudflare.ps1` como administrador para DNS, flush DNS y regla de firewall TCP 8787-8806.
- FerrariPOS mantiene el Quick Tunnel automático al iniciar, sin exigir configuración manual de Cloudflare al cliente.
- El enlace público continúa enviándose por el flujo SMTP configurado al ingresar; además se agregó el envío del enlace al abrir una nueva caja.
