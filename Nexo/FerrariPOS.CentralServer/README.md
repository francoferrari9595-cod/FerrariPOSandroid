# FerrariPOS Central Server

Servidor central inicial para la arquitectura de producción.

- `/health` salud pública.
- `/api/v1/stores/register` registra automáticamente una instalación.
- `/api/v1/stores/{storeId}/snapshot` permite guardar/recuperar un snapshot autorizado.
- SQLite central con WAL.

Este servicio es la primera capa de la migración. La API completa de operaciones de FerrariPOS todavía debe migrarse antes de declarar que la PC puede apagarse manteniendo todas las funciones operativas.
