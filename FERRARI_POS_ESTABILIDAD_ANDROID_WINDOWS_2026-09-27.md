# FerrariPOS — estabilidad Android ↔ Windows / crédito

Base: `FerrariPosOne/main`.

Correcciones aplicadas:
- normalización de medios de pago antes de calcular y persistir crédito;
- sincronización de mesas/tickets fuera del hilo de interfaz;
- caché de snapshot de mesas Windows para que las peticiones Android no tengan que invocar al hilo de UI;
- reconstrucción del salón limitada a cambios reales de mesas y como máximo una vez cada 300 ms;
- excepciones de tareas asíncronas observadas y registradas.

Pruebas: compilar Windows/Android en Actions; venta a crédito nueva y mixta; alternar mesas rápidamente; prueba prolongada; revisar `FerrarisPOS_error.log`.
