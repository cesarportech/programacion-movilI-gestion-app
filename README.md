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

Sesión recordada: al iniciar sesión se crea un token aleatorio de 256 bits. El equipo lo guarda en SecureStorage (en Windows, cifrado para el usuario de Windows) y la base solo guarda su SHA-256 (tabla SessionToken). Cerrar la ventana o Salir conserva la sesión; Archivos → Cerrar Sesión borra el token del equipo y de la base. El token no caduca por tiempo.

Contraseñas con PBKDF2 SHA-256 y sal aleatoria. No hay contraseñas predeterminadas. C:/Sistemas nunca se modifica.

## Importar el catálogo del GL2000

Utilerías → Importar Catálogo GL2000 lista los catálogos CAT<empresa><año>.Tps que encuentra en C:\Sistemas\Datos (o permite elegir otro archivo) y copia sus cuentas a la contabilidad activa. Los archivos .Tps se abren solo para lectura con un lector TopSpeed propio (GestionLibros/Data/Import).

- Se importan cuenta, cuenta superior, descripción, fecha de cambio, usuario y status (C = cambiada).
- Las cuentas que ya existen en FREDI se conservan sin cambios; repetir la importación solo agrega las faltantes.
- Todo se guarda en una transacción: si una cuenta no tiene superior o cae bajo una cuenta con movimientos, no se importa nada.
- El nivel del GL2000 empieza en 0; en FREDI la primera cuenta es nivel 1.
- Con catálogos de un ejercicio (CAT<empresa><año>.Tps) también se importan los acumulados: saldo inicial del año y cargos/créditos de cada mes (tabla ImportedBalance). Reimportar el mismo año reemplaza esos acumulados. Los catálogos plantilla (CATEMP…) solo traen cuentas.
- Consulta y Balanza suman los acumulados del último ejercicio importado (≤ año consultado) a los movimientos capturados en FREDI. Saldo inicial del mes = saldo inicial del año + meses anteriores.
- Cada cuenta importada muestra su propio acumulado del GL, también las superiores, porque el GL no siempre mantiene una cuenta superior igual a la suma de sus subcuentas (p. ej. 1.14900 en la empresa 003). Las superiores sin acumulado propio suman a sus subcuentas.
- El Mayor todavía solo muestra movimientos capturados en FREDI; no incluye los acumulados importados.
- No se importan las pólizas del GL2000. Empresas.Tps está cifrado y no se lee.

## Ejecutar

Desde esta carpeta:

```powershell
.\Iniciar-Fredi.ps1
```

El script usa el SDK .NET 10 local de .tools, cierra la copia de FREDI que esté abierta y compila siempre en GestionLibros/bin-detalles. No se deben editar las mismas pólizas desde varias instancias simultáneas en esta fase.

```powershell
.\.tools\dotnet\dotnet.exe run --project Tests/Fredi.Checks.csproj
```

## Verificación

Compilación Windows .NET 10: 0 errores y 0 advertencias. Comprobaciones automatizadas de autenticación, aislamiento, persistencia, validaciones de pólizas, jerarquía, exactitud de saldos, edición y borrado. Las pruebas usan una base sintética temporal, nunca la base de la aplicación. La inspección visual remota está limitada por fallos de captura del escritorio.

## Pendientes

Implementado: Tipos de Pólizas, Duplicar, Impresión de Pólizas, Balance General, Estado de Resultados (mes y acumulado), Sistema de Respaldos, Importar / Exportar Catálogo CSV, Usuarios Conectados e importación del catálogo y acumulados del GL2000. Acumular Saldos no es necesario (los saldos se calculan al consultar).

Pendiente: importar las pólizas del GL2000 (POL/ASI), módulo de bancos y cheques (maestro, captura, impresión, cancelación, conciliación), centros de costo, departamentos, conceptos, cierre anual, comparativo anual, utilerías de mantenimiento de pólizas (renumerar, traspasar, concentrar, arreglar fechas), saldos iniciales, cambios/baja de usuarios, recuperación de contraseña, validar reglas con el programa antiguo y concurrencia entre equipos.


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
