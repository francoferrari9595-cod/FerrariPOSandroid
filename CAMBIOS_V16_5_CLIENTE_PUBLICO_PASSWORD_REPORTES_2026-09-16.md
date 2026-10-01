FERRARI POS MANAGER V16.5 - CORRECCIONES

1) CLIENTE ID 1 / PÚBLICO GENERAL
- ID 1 queda reservado exclusivamente para Público General.
- Al iniciar una versión nueva, solo se normaliza el registro ID 1.
- No se modifican, recrean, renumeran ni reordenan por nombre los demás clientes.
- Los demás clientes conservan sus IDs, créditos, cuentas corrientes e historial.
- Público General siempre aparece primero.
- Público General no puede eliminarse.
- Guardar sobre ID 1 no permite convertirlo en otro cliente: se normaliza a Público General.

2) CONTRASEÑA MAESTRA WINDOWS
- Nueva contraseña maestra: 39242155.
- Las contraseñas individuales existentes de los usuarios se mantienen.
- El acceso valida la contraseña propia del usuario seleccionado O la contraseña maestra nueva.
- No se modifican hashes de contraseñas individuales.

3) REPORTES WINDOWS
- Corregido ReportsForm.cs: la línea del resumen de Ventas por Categoría tenía un literal de cadena partido en dos líneas y provocaba CS1039/CS1002/CS1056.
- Se mantiene el filtro DESDE/HASTA y DEPARTAMENTO/CATEGORÍA.
- El reporte muestra por categoría: efectivo, Mercado Pago, tarjeta, transferencia, crédito y total vendido.

4) GITHUB
- Se conserva el workflow Android + Windows con artefactos separados.
- No se reintroduce ningún paso para unir/guardar Android + Windows juntos.
- Se conserva el BAT de publicación a GitHub y los BAT de compilación existentes.
