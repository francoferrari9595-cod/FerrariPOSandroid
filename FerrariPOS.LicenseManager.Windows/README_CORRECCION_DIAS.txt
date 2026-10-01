# Ferrari'sPOS - Desarrollador de Licencias CORREGIDO

## Problema corregido

El generador anterior enviaba:

- `DurationDays = 90`
- `DurationDays = 100`
- etc.

El cliente existente, cuando recibe ese campo, calcula la fecha de vencimiento usando
`InstalledAtUtc` de la PC receptora. Por eso, si la instalación tenía 43 días, una
licencia de 90 días podía aparecer con aproximadamente 47 días.

### Solución sin tocar FerrariPOS

Este proyecto NO modifica el programa cliente.

Para licencias finitas, el nuevo desarrollador:

1. Calcula una fecha de vencimiento UTC exacta.
2. Guarda esa fecha en `ExpiresAtUtc`.
3. Envía `DurationDays = null`.
4. Firma el payload con la misma clave privada RSA.
5. Mantiene el prefijo `FPOS-LIC-3`.
6. Se autoverifica antes de entregar el token.

El cliente existente, al no encontrar `DurationDays`, utiliza su rama de
compatibilidad basada en `ExpiresAtUtc`.

## Resultado

Si generás una licencia de 90 días con inicio "Ahora", el token contiene una
fecha exacta 90 días después.

No depende de cuántos días lleva instalada la aplicación en la computadora
del cliente.

## IMPORTANTE sobre la entrega

La fecha de inicio seleccionada es parte del cálculo.

- Si elegís "Ahora" y entregás inmediatamente: aproximadamente 90 días.
- Si generás hoy y entregás varios días después: esos días forman parte del
  período porque el vencimiento ya está fijado.
- Si querés preparar una licencia para una entrega futura, podés seleccionar
  una fecha/hora de inicio futura.

## Solapa VERIFICAR / DIAGNÓSTICO

Permite pegar cualquier token FPOS-LIC-3 y comprobar:

- firma RSA;
- acción;
- tipo;
- License ID;
- Machine ID;
- cliente;
- IssuedAtUtc;
- ExpiresAtUtc;
- si DurationDays está NULL;
- días restantes desde el reloj del administrador;
- hash SHA-256 del token.

Si aparece `DurationDays` con un número en un token finito, ese token no fue
generado por esta versión corregida y puede volver a producir el problema
antiguo.

## Seguridad

`private_key.pem` es la clave privada. NO debe distribuirse con FerrariPOS ni
entregarse a los clientes.

El cliente solo necesita su clave pública, que ya está incorporada en el
programa existente.

## Compilación

Requiere .NET 8 SDK en Windows.

### Probar

Ejecutar:

`ABRIR_DESARROLLADOR.bat`

### Crear EXE independiente

Ejecutar:

`PUBLICAR_DESARROLLADOR.bat`

El resultado queda en:

`publish\FerrariPOS_LicenseManager_Corregido.exe`

La clave privada debe permanecer en la carpeta del desarrollador.

## Prueba recomendada antes de usarlo

1. Abrir el desarrollador.
2. Colocar el Machine ID de una instalación de prueba que tenga una antigüedad
   conocida, por ejemplo 40 días.
3. Elegir ACTIVATE.
4. Elegir CUSTOM.
5. Colocar 90 días.
6. Dejar Inicio de vigencia en AHORA.
7. Generar.
8. Abrir VERIFICAR / DIAGNÓSTICO.
9. Pegar el token.
10. Comprobar:
   - `Firma RSA: VÁLIDA`
   - `DurationDays en token: NULL (CORRECTO)`
   - `ExpiresAtUtc en token: ...`
   - `MODO DE VENCIMIENTO: EXACTO`
11. Entregar el token al cliente.
12. En el cliente, comprobar que muestra aproximadamente 90 días,
    independientemente de que la instalación tenga 40, 50 o 80 días.

## Compatibilidad

Se conserva:

- `FPOS-LIC-3`
- RSA SHA-256 PSS
- Machine ID
- ACTIVATE
- RENEW
- DEACTIVATE
- CUSTOM
- ANNUAL
- PERMANENT

No se modifican los archivos del programa FerrariPOS.

## Nota técnica

La corrección se realiza en el formato del token, no en el ejecutable del
cliente. Por eso las versiones Windows 8/8.1 y Windows 10 del programa no
necesitan recompilarse por este cambio.
