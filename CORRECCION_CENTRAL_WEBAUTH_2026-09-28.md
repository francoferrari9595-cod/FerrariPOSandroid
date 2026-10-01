# Corrección Central Server — WebAuth

Se corrigió únicamente el error de compilación introducido en `Program.cs` por declarar `WebAuth` como `record struct` mientras `WebSession` devuelve `WebAuth?`.

La autenticación web necesita acceder a `StoreId`, `StoreName` y `Username` después de comprobar si la sesión es nula. `WebAuth` ahora es un `record` de referencia, por lo que el flujo existente `if (auth is null) return Results.Unauthorized();` mantiene su comportamiento y permite acceder correctamente a esas propiedades.

No se recuperaron archivos de versiones anteriores ni se revirtieron correcciones previas. La modificación se realizó sobre la versión base actual del proyecto entregado para esta etapa.
