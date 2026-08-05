# Contenedores FirmaEC descentralizada + signature-api

Este paquete administra tres contenedores (`compose.yaml`):

- `firmaec-wildfly`: WildFly 40.0.1, Java 17, `api.war`,
  `servicio.war` y pgJDBC 42.7.13. Motor de FirmaEC (vendor, Security Data).
- `firmaec-postgresql`: PostgreSQL 17.10 con persistencia en el host. Base de
  datos propia de FirmaEC (`firmadigital`), separada de `dbutasystem`.
- `signature-api`: backend .NET 9 propio (`UtaElectronicSignatureBackend`) —
  orquesta los procesos de firma, habla con RepositoryUta/HrBackend, y recibe
  el callback de FirmaEC al completarse una firma. A diferencia de los otros
  dos, esta imagen **no se descarga**, se **construye desde el código fuente**
  (ver sección dedicada más abajo) — es la única de las tres con esa
  particularidad de build.

Apache y `react-nginx-app` no forman parte de este Compose y no son
modificados por la instalación.

## Rutas

- Definiciones: `/opt/uta-containers/electronic-signature`.
- PostgreSQL: `/var/lib/uta-containers/firmaec-postgresql`.
- Respaldos: `/var/backups/uta-containers/firmaec-postgresql`.
- WildFly: `127.0.0.1:8180`, sin exposición pública.
- PostgreSQL 5432: únicamente en la red privada `uta-firmaec-db`.
- Administración WildFly 9990: no publicada.

## Artefactos

Copiar antes de instalar:

```bash
cp FirmaEC/firmadigital-api/target/api.war artifacts/
cp FirmaEC/firmadigital-servicio/target/servicio.war artifacts/
```

El instalador descarga pgJDBC y comprueba su SHA-256.

## Instalación

```bash
chmod +x scripts/*.sh wildfly/entrypoint.sh
./scripts/install-firmaec.sh install
```

## Administración

```bash
./scripts/install-firmaec.sh status
./scripts/install-firmaec.sh start
./scripts/install-firmaec.sh stop
./scripts/install-firmaec.sh restart
./scripts/install-firmaec.sh logs

./scripts/install-firmaec.sh postgres-status
./scripts/install-firmaec.sh postgres-restart

./scripts/install-firmaec.sh wildfly-status
./scripts/install-firmaec.sh wildfly-restart
./scripts/install-firmaec.sh wildfly-logs
```

## signature-api (backend .NET)

A diferencia de WildFly y PostgreSQL, esta imagen no se puede construir desde
este directorio ni descargarse de un registry — su `build.context` en
`compose.yaml` es la raíz del repo completo (`../../..`, para incluir `src/`),
así que **solo compila desde una copia completa del repositorio**
(`/opt/uta-containers/releases/signature-api-<timestamp>/`, ver el flujo de
release documentado para este proyecto). Construir/desplegar un cambio de
código:

```bash
# Desde cualquier lugar, indicando la release con el repo completo
# (o sin argumento: usa la más reciente en /opt/uta-containers/releases/)
/opt/uta-containers/electronic-signature/scripts/build-signature-api.sh \
  /opt/uta-containers/releases/signature-api-20260729-134533
```

Ese script construye la imagen y recrea el contenedor. Para cambios de código
puntuales: copiar los `.cs` cambiados a la release (mismo path relativo bajo
`src/`) y volver a correr el script — no hace falta una release nueva completa
para cada cambio chico.

Gestión normal (una vez que la imagen ya existe):

```bash
./scripts/install-firmaec.sh signature-api-start
./scripts/install-firmaec.sh signature-api-stop
./scripts/install-firmaec.sh signature-api-restart
./scripts/install-firmaec.sh signature-api-status
./scripts/install-firmaec.sh signature-api-logs
./scripts/install-firmaec.sh signature-api-shell
```

Secretos propios de signature-api (además de los de FirmaEC/PostgreSQL) —
**estos dos no se autogeneran, deben ser los valores reales del ambiente**:

```text
secrets/signature_database_connection       # cadena de conexión SQL Server (dbutasystem)
secrets/repositoryuta_service_client_secret # secreto del cliente "uta-signature" en RepositoryUta
```

Permisos: `0640 root:docker`, igual que los demás secretos de este directorio.

## Acceso a consola

Ruta principal en el servidor:

```bash
cd /opt/uta-containers/electronic-signature
```

Sesión de seguimiento:

```bash
tmux attach -t firmaec
```

JBoss CLI:

```bash
./scripts/install-firmaec.sh wildfly-cli
```

Comandos útiles dentro de JBoss CLI:

```text
deployment-info
/subsystem=datasources/data-source=FirmaDigitalDS:test-connection-in-pool
:read-attribute(name=server-state)
quit
```

Terminal del contenedor y consola PostgreSQL:

```bash
./scripts/install-firmaec.sh wildfly-shell
./scripts/install-firmaec.sh postgres-shell
```

También se puede mostrar toda esta ayuda mediante:

```bash
./scripts/install-firmaec.sh console-help
```

La consola web administrativa WildFly, puerto 9990, no se publica por
seguridad.

### Acceso web temporal a WildFly

La consola puede habilitarse excepcionalmente para una única IP y por tiempo
limitado. El puerto 9990 continúa ligado a `127.0.0.1`; Apache es el único
punto HTTPS público.

```bash
./scripts/install-firmaec.sh console-enable DIRECCION_IP 120
./scripts/install-firmaec.sh console-status
./scripts/install-firmaec.sh console-credentials
```

URL:

```text
https://portal.uta.edu.ec/firmaec-console/
```

El acceso exige simultáneamente:

- origen desde la IP autorizada;
- no haber superado la hora de vencimiento;
- usuario y contraseña de administración WildFly.

WildFly permite como origen administrativo exclusivamente
`https://portal.uta.edu.ec`. Apache no replica la autenticación Digest:
la consola y `/management` deben autenticarse directamente contra WildFly
para evitar credenciales y nonces incompatibles.

Despublicar inmediatamente:

```bash
./scripts/install-firmaec.sh console-disable
```

La tarea programada retira el proxy al vencer. Además, Apache contiene una
condición horaria que deniega el acceso incluso si la tarea de limpieza
fallara. Nunca se debe publicar directamente `0.0.0.0:9990`.

## Proxy HTTPS

La API pública se publica así:

```text
https://portal.uta.edu.ec/firmaec/
                  |
                  +--> http://127.0.0.1:8180/api/
```

Instalar o comprobar la ruta:

```bash
./scripts/install-firmaec.sh proxy-install
./scripts/install-firmaec.sh proxy-status
```

Ejemplo de servicio:

```text
POST https://portal.uta.edu.ec/firmaec/version
Content-Type: application/x-www-form-urlencoded
```

La ruta `/servicio`, PostgreSQL 5432 y la administración WildFly 9990
permanecen privadas. El endpoint es una API REST y no contiene una interfaz
web de administración.

Para retirar únicamente el bloque administrado del proxy:

```bash
./scripts/install-firmaec.sh proxy-remove
```

El script realiza un respaldo de `uta-proxy.conf`, valida con
`apachectl configtest` y solo después recarga Apache.

## Docker

```bash
./scripts/install-firmaec.sh docker-start
./scripts/install-firmaec.sh docker-status
```

## Distribución de memoria para servidor de 8 GiB

Los límites se calcularon después de medir el consumo real y los máximos de
cgroups:

| Componente | Reserva | Límite | Configuración principal |
|---|---:|---:|---|
| WildFly/FirmaEC | 1 GiB | 2.25 GiB | Heap 384 MiB–1.25 GiB; metaspace 384 MiB |
| PostgreSQL | 256 MiB | 640 MiB | `shared_buffers=128MB`; 30 conexiones |
| Ubuntu, Apache, Docker, React y caché | — | 4.86 GiB restantes | Incluye espacio para el backend .NET |

En WildFly y PostgreSQL el total de memoria más swap es igual al límite de
RAM. Esto evita que el procesamiento de documentos se degrade por intercambio
intensivo a disco. El host conserva su swap para contingencias de los demás
procesos.

Consultar la distribución:

```bash
./scripts/install-firmaec.sh memory-status
```

La reserva no preasigna memoria: es una protección blanda bajo presión. El
límite sí es estricto. Antes de aumentar nuevamente estos valores deben
repetirse pruebas concurrentes con documentos de tamaños representativos.

## Generar y transportar imágenes

Las tres imágenes (WildFly, PostgreSQL y signature-api) se empaquetan juntas.
Si signature-api tiene código sin construir, hacerlo primero (ver sección
anterior) — este paso solo empaqueta imágenes que ya existen localmente, no
construye nada.

```bash
./scripts/install-firmaec.sh export-images /tmp/firmaec-images.tar.gz
scp /tmp/firmaec-images.tar.gz* usuario@SERVIDOR_NUEVO:/tmp/
```

En el servidor nuevo:

```bash
./scripts/install-firmaec.sh import-images /tmp/firmaec-images.tar.gz
```

## Exportar el sistema completo

Con los contenedores funcionando:

```bash
./scripts/export-firmaec.sh
```

El paquete contiene:

- las tres imágenes Docker (WildFly, PostgreSQL, signature-api);
- configuración sin secretos;
- respaldo lógico PostgreSQL;
- sumas SHA-256.

Los secretos nunca se incluyen (incluidos los dos propios de signature-api).
Deben transferirse mediante un canal institucional seguro — ver
`SECRETS-NOT-INCLUDED.txt` dentro del paquete generado para la lista completa.

## Restauración

`import-firmaec.sh` carga las imágenes pero no restaura automáticamente la
base. Esta protección evita sobrescribir datos por error. La restauración se
realizará de forma controlada después de comprobar que la base destino está
vacía y que el respaldo es recuperable.

## Dónde están los logs

Los tres contenedores usan el driver `json-file` de Docker (10 MiB × 5
archivos cada uno, con rotación automática) — no hay volúmenes de log
separados en el host, todo se consulta con `docker logs` o los atajos de abajo:

```bash
./scripts/install-firmaec.sh logs             # los tres contenedores juntos
./scripts/install-firmaec.sh wildfly-logs      # solo WildFly
./scripts/install-firmaec.sh signature-api-logs  # solo signature-api

# Ventana de tiempo específica (útil para reproducir un reporte puntual):
docker logs signature-api --since 2026-07-31T19:00:00 --until 2026-07-31T20:00:00

# Ruta cruda si hace falta grep/procesar fuera de docker logs:
docker inspect signature-api --format '{{.LogPath}}'
```

Fuentes adicionales, fuera de los logs de Docker, útiles para depurar firmas:

- **`firmadigital.log` en la máquina del firmante** (solo cliente de
  escritorio, no aplica a móvil) — generado por `cliente-jar-with-dependencies.jar`
  en la carpeta desde donde se invoca.
- **Tabla `log` de la propia base de FirmaEC** — registra cada firma
  completada con éxito, incluida la columna `sistema operativo` (WINDOWS 11 /
  ANDROID / IOS), muy útil para confirmar si una firma realmente llegó a
  notificar al sistema:
  ```bash
  docker exec firmaec-postgresql psql -U firmadigital -d firmadigital \
    -c "SELECT fecha, sistema, descripcion FROM log ORDER BY fecha DESC LIMIT 20"
  ```
- **`SGN.tbl_SigningParticipants`/`SGN.tbl_SigningSessions`/`SGN.tbl_SigningEvents`**
  en `dbutasystem` (SQL Server) — estado real de cada firma desde el lado de
  signature-api; `tbl_SigningEvents.CorrelationID` distingue si una firma se
  completó por el callback automático (GUID de sesión) o por la carga manual
  del PDF firmado (`upload:<participantId>`).

## Servicios FirmaEC

El contexto `/api` es utilizado por la aplicación de escritorio FirmaEC.
El contexto `/servicio` almacena temporalmente documentos, genera JWT de cinco
minutos, valida las API Keys y se comunica con PostgreSQL.

`api.war` reenvía internamente a:

```text
http://127.0.0.1:8080/servicio
```

## Estado del firmado integral (actualizado 2026-07-31)

Hecho:

- Sistema UTA registrado en la tabla `sistema` (`UTA-SIGNATURE`), con la URL
  de callback correcta (ver más abajo).
- Backend .NET (signature-api) publicado y su callback de FirmaEC
  funcionando — verificado con firmas reales completadas desde escritorio y
  desde celular (esta última, mediante la carga manual del PDF firmado; ver
  la sección de logs de este archivo).
- `UTA_SIGNATURES` registrado y probado en el almacenamiento de HrBackend.
- Pruebas integrales completas con la aplicación de escritorio y con la app
  móvil (con sus limitaciones documentadas — la app móvil de FirmaEC no
  notifica sola al completar una firma, ver sección de logs).

Pendiente (no verificado/no corregido en ninguna sesión hasta ahora):

- Revisar el registro de JWT completos en el código Java del lado de FirmaEC
  (posible exposición de tokens en sus propios logs — no es código de este
  repo, requeriría coordinar con Security Data).
- `portal.uta.edu.ec` no tiene registro DNS público (solo resuelve dentro de
  la red de la UTA) — pendiente de publicarse a internet.
