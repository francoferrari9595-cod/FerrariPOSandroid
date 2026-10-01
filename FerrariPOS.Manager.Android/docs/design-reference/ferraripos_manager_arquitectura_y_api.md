# Especificación Técnica de Arquitectura, API y Manual: FerrariPOS Manager (Android)

## 1. Arquitectura de Conexión y Seguridad

```
[ ANDROID: FerrariPOS Manager ]
        │ (HTTP/REST local WiFi / HTTPS futuro)
        ▼
[ FerrariPOS Local Server API (Ktor / FastAPI / Go / .NET embebido en PC) ]
        │ (Validación de Token, Rol y Concurrencia)
        ▼
[ FerrariPOS Desktop App / Business Engine ]
        │ (Fórmulas de Costo + IVA 21% + Margen + Redondeo)
        ▼
[ SQLite DB (Base de datos local en PC) ]
```

### Protocolo de Vinculación QR
El QR generado por FerrariPOS de escritorio entrega el siguiente JSON (cifrado o con token pre-compartido):
```json
{
  "app": "FerrariPOS",
  "version": 1,
  "api_version": "1.0",
  "host": "192.168.0.102",
  "port": 5080,
  "token": "pos_pair_sec_994821a8f9c",
  "store_id": "STORE_001",
  "store_name": "Supermercado & Distribuidora Ferrari"
}
```

---

## 2. Definición de Endpoints de la API (`FerrariPosApiService`)

- `GET /api/v1/health`: Comprobación de estado del servidor local y versión compatible (`min_client_version`).
- `POST /api/v1/auth/login`: Autenticación con credenciales (usuario + contraseña + pairing token del QR). Retorna JWT de sesión con claims de permisos (`ROLE_ADMIN` vs `ROLE_CASHIER`).
- `GET /api/v1/dashboard/summary`: Estadísticas del día en tiempo real (Ventas totales, Tickets, Efectivo, Mercado Pago, Cuentas Corrientes, Alertas de stock bajo).
- `GET /api/v1/products`: Listado, búsqueda y filtrado por código de barras, descripción y categoría.
- `GET /api/v1/products/{barcode}`: Consulta puntual vía escáner de cámara.
- `PUT /api/v1/products/{id}/price`: Actualización atómica de precios (costo, ganancia, precio final con IVA 21% y redondeo) con control de concurrencia mediante cabecera `If-Match: "{version_etag}"` o `version_id`.
- `GET /api/v1/inventory`: Listado de existencias con alertas de stock crítico.
- `GET /api/v1/cash`: Resumen y arqueo de caja del turno activo.
- `GET /api/v1/clients`: Clientes con saldo y deudas en cuenta corriente.
- `GET /api/v1/sales`: Historial de ventas y tickets discriminados por medio de cobro.
- `GET /api/v1/reports`: Reportes analíticos agrupados por período (Hoy, Ayer, Semana, Mes).
- `POST /api/v1/audit/log`: Registro automático de acciones administrativas remotas ejecutadas desde el móvil.

---

## 3. Workflow de GitHub Actions (`.github/workflows/build-apk.yml`)

```yaml
name: FerrariPOS Manager - Build APK

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Repository
        uses: actions/checkout@v4

      - name: Set up JDK 17
        uses: actions/setup-java@v4
        with:
          java-version: '17'
          distribution: 'temurin'
          cache: gradle

      - name: Grant execute permission for gradlew
        run: chmod +x gradlew

      - name: Build Debug APK
        run: ./gradlew assembleDebug --stacktrace

      - name: Build Release APK (Unsigned)
        run: ./gradlew assembleRelease --stacktrace

      - name: Upload Debug APK
        uses: actions/upload-artifact@v4
        with:
          name: FerrariPOS-Manager-Debug.apk
          path: app/build/outputs/apk/debug/app-debug.apk

      - name: Upload Release APK
        uses: actions/upload-artifact@v4
        with:
          name: FerrariPOS-Manager-Release.apk
          path: app/build/outputs/apk/release/app-release-unsigned.apk
```

---

## 4. Manual de Usuario (Resumen Operativo)

1. **Instalación:** Descargar e instalar `FerrariPOS Manager.apk` en el dispositivo Android.
2. **Encendido:** Abrir FerrariPOS en la PC principal conectada a la misma red WiFi local.
3. **Vincular Teléfono:** En el menú superior de FerrariPOS PC, seleccionar *"Vincular Teléfono / Acceso Móvil"*.
4. **Escaneo QR:** Abrir la app en Android y pulsar *"ESCANEAR QR"*. Apuntar la cámara a la pantalla de la PC.
5. **Autenticación:** Ingresar el usuario y clave de administrador o cajero.
6. **Uso en Local:** Administrar precios con cálculo automático de IVA y márgenes, escanear códigos de barras de góndola y monitorear caja y ventas en vivo.
