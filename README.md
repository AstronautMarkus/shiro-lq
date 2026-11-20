# ShiroLQ

Sitio web del canal ShiroLQ: videos Loquendo, GTA y una sección de descargas de mods (mods, skins, mapas, objetos). Hecho con Blazor (.NET 10) y MySQL.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MySQL 8 (o MariaDB compatible)

## Estructura

```
shiro-lq/
  src/
    Components/   Páginas y layouts de Blazor (el panel de admin está en Pages/Panel)
    Data/         Acceso a MySQL (ModStore, AdminStore, ChannelStore, VideoStore), sincronización con YouTube (YouTubeSync) y almacenamiento de archivos (S3 o disco)
    Models/       Modelos (Mod, ModType, Admin, Channel, Video)
    wwwroot/      archivos estáticos: css/, js/ e img/ (imágenes de decoración)
    uploads/      Archivos subidos, solo con almacenamiento Local (no se sube a git)
    Program.cs
    shiro-lq.csproj
```

## Configuración

### 1. Crear la base de datos

Las tablas se crean solas al arrancar la app (y una tabla `mods` de una versión anterior recibe sola las columnas nuevas), pero la base de datos tiene que existir:

```sql
CREATE DATABASE shiro_lq;
```

### 2. Cadena de conexión con user-secrets

La cadena de conexión no va en `appsettings.json` para no subir contraseñas al repositorio. En desarrollo se guarda con los user-secrets de .NET, que quedan fuera del proyecto (en `~/.microsoft/usersecrets/` en macOS y Linux, o en `%APPDATA%\Microsoft\UserSecrets\` en Windows).

Desde la raíz del repositorio:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Port=3306;Database=shiro_lq;User ID=root;Password=tu_contraseña" --project src
```

Comandos útiles:

```bash
dotnet user-secrets list --project src                              # ver los valores guardados
dotnet user-secrets remove "ConnectionStrings:Default" --project src # borrar uno
dotnet user-secrets clear --project src                             # borrar todos
```

Si falta la cadena de conexión, la app no arranca y muestra el comando que hay que ejecutar.

### 3. Almacenamiento de archivos: S3 o Local

Los mods y sus imágenes se pueden guardar en un bucket de AWS S3 o en el disco del servidor. Se elige con `Storage:Provider` y es obligatorio: si falta, la app no arranca y muestra el comando.

**S3** (el normal). El bucket queda privado: la app redirige cada descarga e imagen a una URL firmada que dura 15 minutos.

```bash
dotnet user-secrets set "Storage:Provider" "S3" --project src
dotnet user-secrets set "Storage:S3:Bucket" "nombre-del-bucket" --project src
dotnet user-secrets set "Storage:S3:Region" "us-east-1" --project src
dotnet user-secrets set "Storage:S3:AccessKey" "AKIA..." --project src
dotnet user-secrets set "Storage:S3:SecretKey" "..." --project src
```

El usuario IAM de esas claves necesita `s3:PutObject`, `s3:GetObject` y `s3:DeleteObject` sobre `arn:aws:s3:::nombre-del-bucket/*`.

**Local** (respaldo, por ejemplo sin internet o si AWS falla). Los archivos van a `uploads/` junto a la app:

```bash
dotnet user-secrets set "Storage:Provider" "Local" --project src
```

Se puede cambiar de uno a otro cuando se quiera, pero los archivos no se copian solos: los mods subidos con un proveedor no aparecen con el otro.

### 4. Primer admin

El panel de administración necesita al menos un admin. Si la tabla `admins` está vacía, la app crea el primero al arrancar con estos datos (y si faltan, no arranca y muestra el comando):

```bash
dotnet user-secrets set "Admin:Name" "Shiro" --project src
dotnet user-secrets set "Admin:Username" "shiro" --project src
dotnet user-secrets set "Admin:Email" "shiro@ejemplo.com" --project src
dotnet user-secrets set "Admin:Password" "una-contraseña-larga" --project src
```

Solo se usan esa primera vez. Después se pueden borrar (`dotnet user-secrets remove "Admin:Password" --project src`) y los demás admins se crean desde el panel.

### 5. Ejecutar

```bash
dotnet run --project src
```

La app queda en http://localhost:5032. Para usar HTTPS (https://localhost:7050):

```bash
dotnet run --project src --launch-profile https
```

## Producción

Los user-secrets solo se leen en el entorno `Development`. En producción la cadena de conexión y el almacenamiento se pasan como variables de entorno (el doble guion bajo reemplaza a los dos puntos):

```bash
export ConnectionStrings__Default="Server=...;Database=shiro_lq;User ID=...;Password=..."
export Storage__Provider="S3"
export Storage__S3__Bucket="..." Storage__S3__Region="..." Storage__S3__AccessKey="..." Storage__S3__SecretKey="..."
dotnet publish src -c Release -o publish
cd publish && dotnet shiro-lq.dll
```

## Panel de administración

En `/admin` (no hay enlace desde el sitio público). Pide iniciar sesión en `/admin/login` y tiene su propia hoja de estilos y JavaScript (`wwwroot/css/admin.css` y `wwwroot/js/admin.js`). Las acciones se confirman en modales y el resultado se avisa con notificaciones; no usa los `alert`/`confirm` del navegador.

- **Mods**: crear, editar y eliminar. El editor muestra una vista previa en vivo de cómo se verá el mod en el sitio y de las imágenes elegidas antes de subirlas. Los archivos se pueden arrastrar, y cada cosa elegida se puede quitar o deshacer antes de guardar. Al editar, el archivo y la miniatura son opcionales (si no se eligen, se quedan los actuales) y las fotos se quitan o agregan de a una. Al guardar se ve el progreso de la subida y se puede cancelar: no queda nada guardado a medias. Si sales con cambios sin guardar, pide confirmación. Al eliminar un mod también se borra su carpeta del almacenamiento.
- **Visibilidad y autor**: cada mod guarda quién lo publicó (`author_id`, la cuenta que lo creó) y si es público o privado. Los privados no salen en el sitio, y su página, descarga e imágenes dan 404 a quien no haya iniciado sesión; con sesión iniciada se ven con un aviso. Si se borra la cuenta del autor, sus mods se quedan, sin autor (`ON DELETE SET NULL`). Cualquier cuenta puede editar cualquier mod: el autor es solo informativo.
- **Canales**: los canales de YouTube del sitio, en un modal (nombre, enlace y descripción opcional). El enlace tiene que ser del tipo `https://www.youtube.com/@usuario` o `https://www.youtube.com/channel/UC...`; lo que venga después (`/videos`, `?si=...`) se ignora. Al guardar se traen sus videos al tiro, y "Sincronizar videos" los actualiza todos sin esperar a la sincronización automática. La tabla muestra cuántos videos tiene cada canal en el sitio. Siempre hay un canal principal, el canal oficial: el primero que se agrega lo es, se cambia con "Hacer principal" y no se puede eliminar sin antes hacer principal a otro. Un enlace ya agregado no se repite.
- **Admins**: crear, editar (en un modal) y eliminar. Al editar, la contraseña vacía deja la actual. Nadie puede borrarse a sí mismo, y un admin borrado pierde el acceso en su siguiente petición.

Las contraseñas se guardan con hash (PBKDF2 de ASP.NET Core). El login acepta 10 peticiones por minuto por IP; detrás de un proxy inverso todas llegan con la IP del proxy y comparten ese límite.

## Videos

Los videos no se suben al sitio: se copian de YouTube, sin clave de API. Al arrancar la app y después cada 15 minutos, `YouTubeSync` lee el feed RSS público de cada canal (`youtube.com/feeds/videos.xml?channel_id=UC...`) y guarda sus videos en la tabla `videos` (solo id, título y fecha; la miniatura y el reproductor salen de YouTube). Para un enlace `@usuario`, el id del canal se saca una vez de la página del canal y queda en memoria hasta que la app se reinicia.

El feed solo trae los **15 videos más recientes** de cada canal:

- El sitio guarda todos los que va viendo, así que la videoteca crece sola desde que se agrega el canal. Los videos más antiguos que esos 15 nunca entran.
- Un video nuevo aparece solo y un título editado se actualiza. Uno borrado o puesto en privado desaparece del sitio si estaba entre esos 15; si era más antiguo, se queda.
- Si la sincronización de un canal falla (enlace malo, YouTube caído, sin internet), queda en el log y el sitio sigue mostrando los videos que tenía.

- **Inicio**: reproduce el video más nuevo del canal principal y enlaza los 3 siguientes. La franja "Última hora" anuncia ese video ("Nuevo video en el canal" la primera semana, "Último video" después), y los botones "Suscribirme al canal" y "YouTube" llevan al canal principal.
- **Videos**: todos los videos, del más nuevo al más antiguo. Con más de un canal aparecen botones para filtrar por canal (`/videos?channel={id}`), con la descripción del canal elegido.

Las fechas se muestran en UTC: un video subido de noche en América puede salir con la fecha del día siguiente.

## Archivos subidos

Cada mod tiene un id UUIDv7 (por ejemplo `/mods/01a1eb1c-7089-744b-8b8b-5af54febaecf`), guardado en MySQL como `BINARY(16)`. Para verlo legible en una consulta: `SELECT BIN_TO_UUID(id), name FROM mods;`.

Todo lo de un mod va en su propia carpeta, con las mismas rutas en los dos proveedores (en S3 son prefijos del bucket, en Local carpetas dentro de `uploads/`):

```
mods/{id}/
  files/    el archivo del mod. Solo se baja desde /mods/{id}/download
  images/   miniatura y fotos, servidas públicamente en /mods/{id}/images/{nombre}
```

Al eliminar un mod desde el panel se borra su carpeta completa.

Con Local, `uploads/` no está en git. Si se borra, los mods quedan en la base de datos pero sin archivos, así que al publicar hay que conservarla entre despliegues. Con S3 pasa lo mismo si se vacía el bucket.

## Créditos de imágenes

Las imágenes de decoración están en `src/wwwroot/img/`. Son material de fans y no tienen licencia libre:

- `hero-gta.jpg`: captura de GTA III del mod [Playa2 As A Pedestrian](https://www.gtagarage.com/mods/show.php?id=26715) de ClaudeVercetti2014 (el skin "Claudio Slow" de los videos Loquendo), recortada y teñida de azul.
- `walfa-ichirin.png`, `walfa-seija.png` y `pfp.png`: sprites de [Walfas Create](https://walfas.org/), recortados con fondo transparente.
- `pfp.png` es una foto de perfil temporal. Se reemplaza con el avatar real del canal usando el mismo nombre de archivo.
