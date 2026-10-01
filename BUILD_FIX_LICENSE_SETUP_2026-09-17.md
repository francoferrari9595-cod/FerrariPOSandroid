Corrección de compilación

1. El instalador de License Manager ya no busca FerrariPOS_icono.ico dentro de publish. Usa el icono que está junto al .iss, que sí está versionado en el proyecto.
2. Se verifica el icono antes de ejecutar Inno Setup.
3. ReportService mantiene sessionId en CashSessionDetailed; el filtro por fecha usa d únicamente dentro de DailyDetailed.
4. El workflow publica el Manager Windows desde FerrarisPOS.csproj y genera el paquete EXE + SETUP.
