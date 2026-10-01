# FerrariPOS — Etapa 3: PostgreSQL + multi-tienda + heartbeat

Esta entrega parte exactamente de la versión anterior corregida.

## Qué se agregó

- El servidor central ya no depende de SQLite local para el registro de tiendas.
- PostgreSQL es obligatorio para que los registros sobrevivan a reinicios/despliegues.
- `DATABASE_URL` se obtiene desde Render.
- Registro idempotente por `installation_id`.
- `store_id` estable por instalación.
- Token independiente por tienda.
- Endpoint de heartbeat cada 60 segundos.
- Windows continúa funcionando localmente si Internet/Render falla.
- Reintento automático sin bloquear la interfaz.
- Health check verifica también la base central.
- La arquitectura queda lista para migrar operaciones reales a tenant/store_id.

## Render

Si el servicio existente ya está creado, crear un PostgreSQL llamado `ferraripos-central-db`
y agregar al Web Service la variable:

DATABASE_URL = Internal Database URL del PostgreSQL.

Luego hacer Manual Deploy / Deploy latest commit.

El `render.yaml` sirve para una instalación nueva.

## IMPORTANTE

Todavía NO se considera terminada la migración offline/cloud:
mesas, ventas, clientes, crédito, productos y panel deben migrarse a endpoints
tenant-aware antes de retirar la operación local.

## Prueba de aislamiento obligatoria

Antes de vender:
1. Registrar Tienda A.
2. Registrar Tienda B.
3. Comprobar tokens distintos.
4. Comprobar store_id distintos.
5. Intentar consultar recursos de A usando token de B: debe responder 401.
6. Repetir para todos los módulos migrados.
