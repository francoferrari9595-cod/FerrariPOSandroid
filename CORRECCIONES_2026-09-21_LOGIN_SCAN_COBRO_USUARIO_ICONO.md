# FerrariPOS · correcciones 2026-09-21

- Android Manager inicia por defecto con tema **Rojo Ferrari** en instalaciones/migraciones que todavía no tienen un tema elegido explícitamente.
- Botón **ESCANEAR** de Ventas cambiado a rojo Ferrari con doble halo y estela neón animada.
- Cobro de tickets Windows desde Android desacoplado del diálogo del ticket: el diálogo de pago ya no se monta dentro de otro AlertDialog. Esto evita la destrucción simultánea de ventanas modales que provocaba el cierre forzado después de cobrar.
- Callback de cobro ejecutado directamente en el contexto principal de Compose, sin publicar un Runnable adicional sobre el diálogo.
- Los tickets Windows cobrados desde Android continúan sin Toast/sonido de confirmación en Android; Windows recibe su propia notificación.
- Cambio de usuario: selector desplegable, contraseña del usuario o contraseña maestra de Windows, comprobación del estado de caja y cierre seguro del diálogo después de autenticar.
- Login Windows: imagen `FerrariPOS_login_poster.png` y efectos neón ahora se dibujan en un único control para que los reflejos no oculten la imagen. Se agregaron estelas roja Ferrari, cian, blanca y reflejos dorados animados.
- License Admin Android: se restauró el icono mediante `android:icon` y `android:roundIcon` apuntando a `ferrari_app_icon`.
- La eliminación de promociones ya exige motivo y se mantiene registrada para el reporte/cierre mediante `MOBILE_PROMOTION_DELETE`.
