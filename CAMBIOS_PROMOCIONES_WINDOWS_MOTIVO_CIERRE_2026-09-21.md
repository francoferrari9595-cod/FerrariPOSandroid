# FerrariPOS Windows — promociones: motivo obligatorio y cierre

- Al eliminar una promoción desde Windows, ahora se solicita obligatoriamente el motivo.
- La eliminación se cancela si el motivo queda vacío.
- La confirmación muestra promoción y motivo antes de borrar.
- Se registra en `audit_log` como `PROMOTION_DELETE`, con usuario, promoción, ID y motivo.
- `ReportService.CashSessionDetailed` incluye las promociones eliminadas durante el turno con fecha/hora, usuario y motivo.
- `CashClosingPdfService` incluye las promociones eliminadas desde Windows en el cierre PDF/email, separadas de las eliminaciones realizadas desde Manager Android.
- No se modifica la lógica de promociones, precios ni productos asociados salvo el requisito de auditoría.
