# ShiroLQ

Sitio web del canal ShiroLQ: videos Loquendo, GTA y una sección de descargas de mods (mods, skins, mapas, objetos). Hecho con Blazor (.NET 10) y MySQL.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MySQL 8 (o MariaDB compatible)

## Estructura

```
shiro-lq/
  src/
    Components/   Páginas y layout de Blazor
    Data/         Acceso a MySQL (ModStore)
    Models/       Modelos (Mod, ModType)
    wwwroot/      CSS y archivos estáticos
    uploads/      Archivos subidos por los usuarios (no se sube a git)
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

### 3. Ejecutar

```bash
dotnet run --project src
```

La app queda en http://localhost:5032. Para usar HTTPS (https://localhost:7050):

```bash
dotnet run --project src --launch-profile https
```

## Producción

Los user-secrets solo se leen en el entorno `Development`. En producción la cadena de conexión se pasa como variable de entorno (el doble guion bajo reemplaza a los dos puntos):

```bash
export ConnectionStrings__Default="Server=...;Database=shiro_lq;User ID=...;Password=..."
dotnet publish src -c Release -o publish
cd publish && dotnet shiro-lq.dll
```

## Archivos subidos

Los mods y sus imágenes se guardan en disco dentro de `uploads/`, junto a la app:

- `uploads/mods/`: archivos de los mods. Solo se pueden bajar desde `/mods/{id}/download`.
- `uploads/images/`: miniaturas y fotos, servidas públicamente en `/images`.

Esta carpeta no está en git. Si se borra, los mods quedan en la base de datos pero sin archivos. Al publicar hay que conservarla entre despliegues.
