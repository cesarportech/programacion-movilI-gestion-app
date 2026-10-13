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
- Al cancelar o salir con cambios se solicita Guardar, Descartar o Continuar editando. Guardar valida y acepta la póliza; si no cuadra, permanece abierta. Quitar movimientos solo afecta saldos al aceptar.

## Alcance local

La base se guarda en FileSystem.AppDataDirectory. Diferentes usuarios de la aplicación comparten datos en la misma instalación y perfil de Windows. Otros equipos o perfiles tienen bases independientes. No compartir SQLite por red. La API y base central son necesarias para uso distribuido. Quien tenga acceso al archivo local puede leerlo directamente.

Sesión recordada: al iniciar sesión se crea un token aleatorio de 256 bits. El equipo lo guarda en SecureStorage (en Windows, cifrado para el usuario de Windows) y la base solo guarda su SHA-256 (tabla SessionToken). Cerrar la ventana o Salir permite recuperar la sesión únicamente mientras siga vigente; Archivos → Cerrar Sesión borra el token del equipo y de la base. La duración máxima es de 8 horas desde el login y el bloqueo por inactividad ocurre a los 15 minutos. Reabrir no reinicia las 8 horas. Los tokens anteriores a esta actualización exigen un nuevo login.

Cada operación protegida verifica el usuario almacenado, su versión de seguridad y los plazos de sesión; las escrituras transaccionales vuelven a verificar antes de guardar. Cambiar contraseña (también mediante la herramienta de recuperación), suspender o eliminar al usuario invalida sus sesiones abiertas. Suspender / Activar está disponible para el maestro en Usuarios; reactivar exige un login nuevo.

La ventana revisa la vigencia cada 5 segundos y al recuperar el foco, sin renovar la actividad. Ante revocación o vencimiento se limpia el token y se vuelve al login; los cambios pendientes quedan en el último borrador guardado para recuperación autorizada. En Windows, las pulsaciones de teclado y clics renuevan la actividad, además de las operaciones protegidas. En otras plataformas, la actividad se registra al operar; la captura de entrada nativa se implementó para Windows. Leer sin interactuar durante 15 minutos también bloquea la sesión. Los controles de acceso operan sobre la misma base local compartida por las instancias: copias independientes no comparten revocaciones.

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
- Consulta, Mayor individual, Auxiliares y Balanza usan el mismo cálculo de saldos locales e importados. Los acumulados GL2000 se identifican por mes/año, sin inventar partidas diarias.
- Fechas parciales: si el rango corta un mes con actividad importada, se solicita el mes completo. Sin actividad importada en ese mes se permiten fechas exactas. Con saldos importados, consulte un ejercicio por vez; cada apertura anual puede reemplazar el historial previo.
- Los acumulados propios de una cuenta superior prevalecen sobre los de sus descendientes; las partidas locales se suman por la jerarquía real. Los saldos intermedios antes del acumulado mensual no son saldos diarios completos.
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


## Protección de captura y recuperación

La cabecera, los movimientos y el texto incompleto de la ventana de movimiento se conservan en JournalDraft al editar. Esta tabla no participa en saldos ni reportes. Cada borrador pertenece a un usuario y contabilidad; se ofrece recuperarlo al entrar nuevamente en Agregar o Cambiar la misma póliza. Abrir sin recuperar conserva los borradores existentes.

Cancelar, Salir, volver atrás, cerrar sesión y cerrar la ventana de Windows solicitan Guardar, Descartar o Continuar editando cuando hay cambios pendientes. También se protege Cancelar y la X del movimiento. Guardar en el movimiento lo incorpora a la póliza todavía sin publicar; Guardar en la salida del editor valida y publica la póliza completa. Si falla la validación, la salida se cancela.

Aceptar una póliza elimina su borrador en la misma transacción. Un borrador de edición no puede sobrescribir una póliza que cambió posteriormente. Descartar elimina solo el borrador seleccionado. Ante revocación o vencimiento de sesión se conserva el último borrador persistido sin permitir publicar con acceso retirado. La recuperación frente a un cierre inesperado alcanza los cambios guardados correctamente en disco; un fallo de almacenamiento puede impedirla y se muestra un aviso.

Inicio normal: `Iniciar-Fredi.ps1` abre el ejecutable ya compilado. Si FREDI está abierto, lo informa y no termina ningún proceso. Compilación: `Compilar-Fredi.ps1` se ejecuta con la aplicación cerrada; si está abierta, se detiene con instrucciones para guardar y cerrar. Nunca fuerza el cierre.


## Corte de migración y lotes

Antes de importar más saldos, configure **Utilerías → Corte y lotes de migración**. GL2000 cubre hasta el fin de mes elegido y FREDI comienza al día siguiente. Las pólizas locales solapadas impiden fijar el corte; los acumulados antiguos sin corte requieren revisión antes de calcular saldos. No se asigna ninguna fecha automáticamente.

Los lotes repetidos no duplican importes. El detalle mensual normalizado solo sustituye acumulados cuando coinciden exactamente los cargos y créditos, manteniendo evidencia y procedencia. Instrucciones, límites y formato JSON: [MIGRACION.md](MIGRACION.md).

### Desglose desde Consulta
Un clic en una cuenta abre Desglose de Movimientos para el mes seleccionado. Pulse una referencia, cargo o crédito, o use Ver partida, para consultar la póliza completa con el movimiento resaltado. Volver conserva el desglose. La partida se abre en modo de consulta y no crea borradores ni modifica saldos. Las filas de acumulados GL2000 sin detalle se identifican y explican por qué no tienen una partida individual disponible.


Consulta con migración pendiente: muestra las cuentas por origen (GL2000 importado o Partidas FREDI), sin sumar ambas fuentes. El origen se conserva en el desglose y se identifica en las exportaciones. Los reportes consolidados y las escrituras conservan la validación del corte. Para navegar al detalle antiguo se necesitan pólizas históricas importadas y conciliadas; los acumulados mensuales no contienen referencias individuales.


Catálogos precargados: el maestro dispone de Archivos → Catálogos precargados. Los paquetes solo incluyen código, cuenta superior y descripción; son versiones inmutables. Una contabilidad vinculada mediante CatalogPackageId rechaza altas, cambios, bajas e importaciones de cuentas. El maestro selecciona obligatoriamente el catálogo al crear la contabilidad y después le asigna el usuario. El usuario no elige ni cambia contabilidad o catálogo. La creación copia las cuentas sin saldos y fija el paquete de forma atómica.



En Maestro → Catálogos puede agregar archivos GL2000 como paquetes internos sin saldos, revisar sus cuentas y eliminar paquetes que no estén asignados. La comprobación de asignación se repite dentro de la transacción para impedir borrar un catálogo usado por una contabilidad. Al regresar a Administración se actualiza el selector de catálogos.


Captura por teclado: Enter recorre Tipo → Referencia → Fecha → Concepto → movimiento y abre la póliza seleccionada en la tabla. En movimientos, Enter recorre Cuenta → Concepto → Valor. Un valor terminado en + agrega el cargo; terminado en - agrega el crédito, sin otro Enter. El movimiento queda en la póliza pendiente; los saldos solo cambian al aceptar la póliza. Las expresiones pueden escribirse entre paréntesis. F12 repite el campo de la captura anterior del usuario y contabilidad; en Referencia propone el siguiente consecutivo. Las nuevas referencias tienen 7 dígitos: año (último dígito), mes (2), consecutivo (4), independiente por tipo y período. Primera propuesta: 0001. Los consecutivos agotados o duplicados se rechazan sin guardar. Las referencias históricas almacenadas no se renumeran.

