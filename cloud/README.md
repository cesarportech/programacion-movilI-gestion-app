# Nube de pruebas FREDI

Proyecto: **VK-FUSSION** (`efekzvmgnicztweluyim`). No es producción.

## Uso

1. Trabaje y guarde las partidas normalmente. SQLite es la base de trabajo y funciona sin internet.
2. Abra **Acumular**, escriba la contraseña del usuario y pulse **Acumular · enviar pendientes**.
3. Espere la confirmación. Ante un corte, vuelva a Acumular. No borre la base local ni sus tablas `Cloud*`.
4. El maestro también dispone de Acumular: debe enviar los cambios de usuarios, permisos, contabilidades y catálogos antes de que los operadores nuevos puedan sincronizar.

La carga inicial de octubre de 2026 contiene dos contabilidades, dos paquetes de catálogo, tres usuarios, 630 cuentas, cuatro tipos de póliza, cinco partidas y diez movimientos. Los borradores se respaldan aparte; los tokens locales no salen del dispositivo. Una partida todavía abierta solo se respalda como borrador cuando el mecanismo local de borradores la guarda.

Esta versión sube datos; las consultas de la aplicación continúan leyendo SQLite. Las rutas permiten consultar la copia remota. **No hay descarga automática ni combinación de escrituras entre computadoras**: solo una instalación nueva descarga sus datos una vez (ver abajo). Si dos instalaciones parten de la misma copia y modifican una contabilidad, el segundo envío recibe conflicto y queda pendiente. No se deben eliminar las revisiones para forzar una sobrescritura. La reconciliación entre equipos que ya tienen datos requiere un procedimiento posterior.

### Equipo nuevo (clientes)

Una instalación sin contabilidades ya no pide crear un maestro. El usuario escribe el usuario y la contraseña que le asignó el maestro, y la app los valida en la nube, descarga lo que ese usuario puede ver y entra con esos mismos datos. El operador recibe su contabilidad, su propio usuario y sus borradores; el maestro recibe todo. Se conservan las revisiones, así que el siguiente Acumular continúa el mismo historial.

- Se necesita internet en el primer ingreso. El maestro debe haber acumulado antes al usuario y su contabilidad.
- Nunca se reemplaza un equipo que ya tiene contabilidades, catálogos, partidas o borradores. Un maestro creado por error en una instalación vacía sí se reemplaza.
- Crear un maestro nuevo solo se ofrece cuando la nube no reconoce el usuario, y se pide confirmación (primera instalación de FREDI).
- Sin conjunto de datos local, la nube identifica el conjunto por el nombre de usuario; debe ser único entre conjuntos.

## Configuración y rutas

La dirección base se cambia desde **maestro → Acumular → Guardar dirección API**. Se guarda en `fredi-api.json` junto a la base local. Por defecto:

`https://efekzvmgnicztweluyim.supabase.co/functions/v1/fredi-api/`

`GestionLibros/Data/CloudApi.cs` concentra las rutas y las peticiones HTTP. No hay claves secretas de Supabase en la aplicación.

| Método | Ruta | Uso |
|---|---|---|
| POST | `v1/auth/login` | Dataset (opcional en un equipo nuevo), usuario, contraseña → token temporal y dataset |
| POST | `v1/auth/logout` | Revocar token |
| POST | `v1/sync` | Enviar un grupo de datos con revisión y UUID de solicitud |
| POST | `v1/status` | Revisiones visibles para el usuario |
| GET | `v1/contabilidades` | Contabilidades autorizadas |
| GET | `v1/contabilidades/{id}` | Registros y revisión de una contabilidad |
| GET | `v1/catalogos` | Biblioteca de catálogos, solo maestro |
| GET | `v1/restore` | Copia completa de lo que el usuario puede ver, con revisiones (equipo nuevo) |

Un nuevo servidor requiere desplegar `schema.sql`, después `gateway.sql`, la función `fredi-api/index.ts` y provisionar de forma administrativa el conjunto de datos y su maestro. Cambiar la URL no copia la base anterior, no reinicia revisiones y no provisiona credenciales. El servidor nuevo debe implementar el mismo contrato y conservar las revisiones si sustituye al anterior. Las URL deben usar HTTPS y no incluir claves. Se deshabilitan redirecciones HTTP para evitar reenviar credenciales a otro destino.

## Persistencia y seguridad

- SQLite registra cambios mediante triggers dentro de la misma transacción que modifica los datos.
- Cada grupo genera una instantánea completa: `admin`, `company:{id}` o `draft:{userId}`. La ausencia de un registro previamente enviado representa una eliminación.
- `CloudPending` conserva el JSON exacto y el UUID hasta recibir ACK. Una respuesta perdida se reintenta con el mismo UUID.
- El ACK solo elimina avisos anteriores al envío: no descarta cambios hechos durante la conexión.
- PostgreSQL bloquea la revisión del grupo, comprueba la versión esperada y reemplaza los registros en una transacción. `fredi_sync_batches` conserva historial de instantáneas, autor y fecha.
- `fredi_records` conserva cada entidad, su identificador local y sus campos en JSONB. No se envía el archivo SQLite como un bloque opaco.
- Todas las tablas tienen RLS y deniegan acceso directo a `anon` y `authenticated`. Solo la función Edge usa el rol de servicio.
- La función Edge usa autenticación propia compatible con los verificadores PBKDF2 SHA-256 existentes (210000 iteraciones). Por ello `verify_jwt=false`; cada ruta protegida valida un token opaco de 256 bits, guardado únicamente como SHA-256 en el servidor.
- Los tokens vencen en una hora o tras 15 minutos sin uso. La app revoca el token al terminar el envío. Suspender, borrar o cambiar la contraseña invalida las sesiones remotas una vez que el maestro envía ese cambio. Sin conexión, otros equipos conservan su estado local hasta una futura sincronización; esta implementación no promete revocación remota instantánea de operaciones locales desconectadas.
- Las contraseñas en texto claro nunca se persisten ni se registran en logs. Los verificadores viajan solo en el grupo administrativo y las tablas privadas de servicio.
- Límite actual: 10 MiB por envío y 100000 registros por grupo. Si se supera, el envío queda pendiente; hará falta particionar por ejercicio antes de usar volúmenes mayores.
- El historial no tiene purga automática en esta etapa de pruebas. Debe definirse retención antes de producción.

## Despliegue y validación

`schema.sql` es la fuente reproducible de las tablas. Aplicarlo con el mecanismo de migraciones de Supabase en un proyecto vacío. `gateway.sql` contiene la función `fredi_gateway`: es idempotente y para actualizarla basta ejecutarlo otra vez en el editor SQL. Actualice primero `gateway.sql` y luego la función Edge. Desplegar `fredi-api/index.ts` con autenticación JWT de plataforma desactivada **solo porque la función implementa la autenticación descrita**. `SUPABASE_URL` y `SUPABASE_SERVICE_ROLE_KEY` permanecen en el entorno de Edge.

`Tests/CloudSyncChecks.cs` comprueba persistencia de pendientes, reintentos, aislamiento, eliminaciones, rollback, cambios concurrentes y conflictos HTTP. `cloud/integration_checks.py` prueba el API real con un dataset sintético y una semilla administrativa separada. No usar los scripts de bootstrap para un servidor que ya tiene datos.

`Tests/CloudBootstrap.cs` y `cloud/bootstrap_local.py` son herramientas de mantenimiento exclusivas de pruebas: no se incluyen en MAUI. La carga inicial se hizo con un respaldo previo, exportación consistente, token temporal de bootstrap revocado y confirmaciones registradas localmente.

La política del entorno bloqueó la eliminación de los archivos auxiliares de esta carga. Permanecen en `%TEMP%` los archivos `fredi-cloud-initial.*`, `fredi-cloud-test.json`, `fredi-cloud-test.sql`, `fredi-cloud-test-v2.json` y `fredi-cloud-test-v2.sql`. La exportación inicial contiene datos contables y verificadores de contraseñas: no compartirla. El token de bootstrap ya está revocado, y los datasets sintéticos fueron retirados de Supabase. El usuario puede eliminar estos archivos temporales manualmente después de revisar la carga.

Documentación de referencia: [autenticación de Edge Functions](https://supabase.com/docs/guides/functions/auth-headers), [protección de la Data API](https://supabase.com/docs/guides/api/securing-your-api).
