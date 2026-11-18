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
    Data/         Acceso a MySQL (ModStore, AdminStore) y almacenamiento de archivos (S3 o disco)
    Models/       Modelos (Mod, ModType, Admin)
    wwwroot/      CSS y archivos estáticos (imágenes de decoración en img/)
    uploads/      Archivos subidos, solo con almacenamiento Local (no se sube a git)
    Program.cs
    shiro-lq.csproj
```

## Configuración

### 1. Crear la base de datos

Las tablas se crean solas al arrancar la app, pero la base de datos tiene que existir:

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

En `/admin` (no hay enlace desde el sitio público). Pide iniciar sesión en `/admin/login` y tiene su propia hoja de estilos (`wwwroot/admin.css`).

- **Mods**: crear, editar y eliminar. Al editar, el archivo, la miniatura y las fotos son opcionales: si no se eligen, se quedan los actuales; si se eligen fotos nuevas, reemplazan todas las anteriores. Al eliminar un mod también se borran sus archivos del almacenamiento.
- **Admins**: crear, editar y eliminar. Al editar, la contraseña vacía deja la actual. Nadie puede borrarse a sí mismo, y un admin borrado pierde el acceso en su siguiente petición.

Las contraseñas se guardan con hash (PBKDF2 de ASP.NET Core). El login acepta 10 peticiones por minuto por IP; detrás de un proxy inverso todas llegan con la IP del proxy y comparten ese límite.

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
