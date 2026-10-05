# FREDI Contabilidad

Aplicación .NET MAUI 10 con SQLite local. El proyecto técnico conserva el nombre GestionLibros; el ejemplo de libros fue reemplazado. Se conserva la base fredi-pruebas-v1.db3 y las tablas existentes; las tablas Journal y JournalLine se agregan automáticamente sin borrar usuarios, contabilidades ni cuentas.

## Recorrido implementado

1. Crear el maestro en la primera ejecución o iniciar sesión.
2. Maestro: entrar a Empresas y usuarios para crear contabilidades y asignar operadores.
3. Pantalla principal: seleccionar contabilidad, mes y año.
4. Catálogo: buscar, agregar cuentas, modificar descripción y borrar cuentas sin hijos ni movimientos.
5. Asientos: recorrer pestañas mensuales, agregar, cambiar o borrar pólizas.
6. Póliza: capturar tipo, referencia, fecha y concepto; agregar/cambiar/quitar movimientos en una pantalla separada; revisar Sumas iguales y aceptar.
7. Consulta/Saldos: consultar saldo inicial, cargos, créditos y saldo actual por cuenta. Pulsar una cuenta abre su mayor.
8. Balanza: elegir nivel máximo, incluir u ocultar cuentas sin actividad y consultar saldos deudores/acreedores y totales generales.
9. Mayor: elegir cuenta, fechas e inclusión de subcuentas para ver saldo inicial, movimientos y saldo acumulado.
10. Reportes: vista previa, CSV y HTML imprimible desde el navegador (también permite guardar PDF).

Las tablas amarillas, encabezados azules, formularios verdes, nombres y organización toman como referencia las capturas del sistema antiguo. La reproducción todavía es parcial: faltan columnas de banco/beneficiario, auditoría de cuentas, atajos y otros menús. Los reportes actuales son formatos nuevos basados en los datos de FREDI; su equivalencia visual y numérica con los originales sigue pendiente.

## Reglas de esta versión de prueba

- Operadores limitados a su contabilidad, con validación también en el servicio de datos.
- Pólizas y movimientos se guardan, sustituyen o eliminan en una transacción.
- Tipo es un código libre provisional; el catálogo de tipos está pendiente.
- Referencia única por contabilidad, ejercicio y tipo.
- Fecha determina el mes/año del listado; una póliza fechada fuera del mes activo aparece en el mes de su fecha.
- Cada movimiento tiene un cargo o crédito positivo, máximo dos decimales y límite 9,999,999,999.99. Se almacenan enteros en centavos.
- Al menos dos movimientos y cargos iguales a créditos al guardar.
- Movimientos únicamente en cuentas de detalle. No se pueden agregar subcuentas a una cuenta con movimientos ni borrar cuentas utilizadas.
- Saldo inicial calculado con movimientos previos al mes; cierre = inicial + cargos - créditos. Deudor positivo, acreedor negativo. Las cuentas superiores suman descendientes.
- Catálogo compartido entre ejercicios dentro de cada contabilidad. La conservación histórica por ejercicio está pendiente de definir antes de migrar.
- Límite provisional de 9 niveles. Las reglas son explícitas para pruebas; no se ha certificado equivalencia con el sistema antiguo.
- Cancelar una póliza descarta el borrador; no modifica la base. Quitar movimientos solo se persiste al aceptar la póliza.

## Alcance local

La base se guarda en FileSystem.AppDataDirectory. Diferentes usuarios de la aplicación comparten datos en la misma instalación y perfil de Windows. Otros equipos o perfiles tienen bases independientes. No compartir SQLite por red. La API y base central son necesarias para uso distribuido. Quien tenga acceso al archivo local puede leerlo directamente.

Contraseñas con PBKDF2 SHA-256 y sal aleatoria. No hay contraseñas predeterminadas. No se importan datos ni se modifica C:/Sistemas.

## Ejecutar

Desde esta carpeta:

```powershell
.\Iniciar-Fredi.ps1
```

El script usa el SDK .NET 10 local de .tools y compila en GestionLibros/bin-reports para no sobrescribir el ejecutable anterior si continúa abierto. Cierre la versión anterior antes de trabajar con la nueva. No se deben editar las mismas pólizas desde varias instancias simultáneas en esta fase.

```powershell
.\.tools\dotnet\dotnet.exe run --project Tests/Fredi.Checks.csproj
```

## Verificación

Compilación Windows .NET 10: 0 errores y 0 advertencias. Comprobaciones automatizadas de autenticación, aislamiento, persistencia, validaciones de pólizas, jerarquía, exactitud de saldos, edición y borrado. Las pruebas usan una base sintética temporal, nunca la base de la aplicación. La inspección visual remota está limitada por fallos de captura del escritorio.

## Pendientes

Validar reglas con el programa antiguo, historial por ejercicio, permisos más finos, concurrencia, auditoría completa, cambios/baja de usuarios, recuperación de contraseña, duplicar pólizas, IVA, bancos, conciliación, acumulación formal, cierre anual, balance general, estado de resultados, comparativo anual, importación de saldos e importación TPS. No se ejecuta un cierre ni se simula la migración.


## Reportes y exportaciones

- La balanza muestra saldos iniciales/finales separados en deudores y acreedores, cargos y créditos. Los totales se calculan solo con cuentas de detalle, independientemente del filtro de nivel. No son la suma de todas las filas visibles, porque las superiores ya incluyen descendientes.
- El mayor incluye movimientos entre ambas fechas, saldo anterior al inicio y saldo después de cada movimiento. Se ordena por fecha, tipo, referencia y claves de póliza/movimiento.
- Se usan relaciones reales de cuenta superior; compartir un prefijo en el código no convierte una cuenta en descendiente.
- Una transacción de lectura mantiene consistente la información de cada reporte.
- CSV y HTML se guardan en la subcarpeta Reportes de FileSystem.AppDataDirectory. La pantalla indica la ruta. Los nombres únicos evitan sobrescribir archivos previos.
- CSV utiliza UTF-8 con BOM, coma como separador y punto decimal. Los códigos deben importarse como texto para conservar ceros iniciales en Excel. Texto que pudiera interpretarse como fórmula se exporta con un apóstrofo protector; los importes negativos siguen siendo numéricos.
- HTML incluye encabezados repetidos y orientación horizontal para imprimir desde el navegador. La impresión no es directa a una impresora: se abre el archivo y el usuario elige Imprimir o Guardar PDF. La casilla B y N de la balanza aplica al HTML imprimible.
- Los reportes contienen solamente movimientos capturados en FREDI; no contienen saldos del sistema antiguo ni cierres automáticos.

## Comprobación de esta etapa

69 comprobaciones automáticas aprobadas (37 anteriores + 32 de reportes). Compilación Windows: 0 errores y 0 advertencias. Se revisaron visualmente las muestras HTML de balanza y mayor y se comprobó una muestra PDF de 7 páginas con encabezados repetidos. No se ha enviado ningún trabajo a una impresora. La navegación nativa completa permanece pendiente de revisión con el usuario por las limitaciones de captura del escritorio.
