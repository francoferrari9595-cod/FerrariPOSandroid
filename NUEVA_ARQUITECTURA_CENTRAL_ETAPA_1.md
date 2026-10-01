# FerrariPOS — Arquitectura central — Etapa 1

Esta versión mantiene la arquitectura Windows/Android actual y agrega el primer servidor central de producción.

## Objetivo

Preparar una URL estable para que Windows, Android y el panel web puedan utilizar un punto central, sin depender de localhost como arquitectura definitiva.

## Incluido

- FerrariPOS.CentralServer (.NET 8).
- Health check `/health`.
- Registro automático de instalación/tienda.
- Identificador estable de tienda.
- Token por tienda.
- Persistencia SQLite central con WAL.
- Endpoint de snapshot autorizado.
- `render.yaml` y Dockerfile para Render.
- BAT de publicación: solo pide el link de GitHub y reemplaza `main` automáticamente.

## Importante

La migración completa de todas las operaciones de mesas, ventas, crédito, inventario, clientes y panel al servidor central NO se declara terminada en esta etapa. Se mantiene la conexión actual para no romper Android/Windows.

La PC podrá apagarse para las funciones ya migradas al servidor central. Las funciones que todavía dependan del SQLite local seguirán necesitando Windows encendido hasta completar la siguiente etapa.
