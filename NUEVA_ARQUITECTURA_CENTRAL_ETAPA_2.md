# FerrariPOS — Etapa 2: conexión central automática

Esta entrega parte de la versión inmediatamente anterior.

## Automatizado para el cliente

- Windows registra automáticamente la instalación en FerrariPOS Central.
- Se conserva un identificador estable por instalación.
- Se guarda el `store_id` y token de la tienda localmente.
- Si Internet/Render no está disponible, FerrariPOS NO se bloquea: funciona localmente y reintenta.
- El servidor central expone configuración, salud y estado.
- CORS queda preparado para el panel web.
- SQLite central usa WAL, busy_timeout y sincronización segura.

## Objetivo de la siguiente migración

La API actual de Windows/Android todavía contiene operaciones locales que deben migrarse una por una al servidor central. No se elimina el mecanismo local hasta terminar esa migración y probar mesas, ventas, crédito, clientes y panel.

## Cliente final

El comprador no configura Render, Cloudflare, puertos, GitHub ni localhost. La conexión central se realiza automáticamente.
