# FtpBridgeFunction

Azure Function (HTTP trigger, .NET 8 isolated worker) que recibe un fichero en
Base64 desde Business Central (AL) y lo sube a un servidor FTP.

## Abrir en Visual Studio 2022

1. Descomprime el zip.
2. Abre `FtpBridgeFunction.csproj` con Visual Studio 2022 (17.8 o superior,
   con la carga de trabajo "Desarrollo de Azure" instalada).
3. Visual Studio restaurará los paquetes NuGet automáticamente al abrir.

## Ejecutar en local

Este proyecto usa `UseDevelopmentStorage=true` en `local.settings.json`, es
decir, **necesita Azurite** (el emulador de Storage) corriendo en local.
Visual Studio 2022 lo arranca automáticamente si tienes instalado el paquete
"Azurite" (Herramientas > Opciones > Azure Storage, o simplemente al pulsar
F5 VS suele ofrecerte instalarlo/arrancarlo). Si prefieres arrancarlo a mano:

```
npm install -g azurite
azurite --silent --location C:\azurite --debug C:\azurite\debug.log
```

Si NO quieres depender de Azurite en absoluto, puedes cambiar en
`local.settings.json`:
```
"AzureWebJobsStorage": "UseDevelopmentStorage=true"
```
por la cadena de conexión real de una Storage Account (Azure Portal >
Storage Account > Claves de acceso > Connection string).

Pulsa **F5** en Visual Studio. La consola mostrará la URL local, algo como:
```
UploadToFtp: [POST] http://localhost:7071/api/UploadToFtp
```

## Configurar el FTP (local)

Edita `local.settings.json` con los datos de tu servidor FTP de pruebas:
```
"FTP_HOST": "ftp.tuservidor.com",
"FTP_USER": "usuario",
"FTP_PASSWORD": "password",
"FTP_FOLDER": "carpeta/destino",
"FTP_USE_SSL": "false"
```

## Probar con curl

```bash
curl -X POST http://localhost:7071/api/UploadToFtp ^
  -H "Content-Type: application/json" ^
  -d "{\"fileName\":\"prueba.xml\",\"base64Content\":\"PHJvb3Q+SG9sYSBNdW5kbzwvcm9vdD4=\"}"
```

(el Base64 de ejemplo corresponde a `<root>Hola Mundo</root>`)

## Publicar a Azure

1. Clic derecho en el proyecto (Explorador de soluciones) > **Publicar**.
2. Elige "Azure" > "Azure Function App (Windows/Linux)".
3. Selecciona tu suscripción y la Function App que creaste en el portal
   (o crea una nueva desde el propio asistente de publicación).
4. Publica.

## Configurar variables de entorno en Azure (producción)

`local.settings.json` NO se despliega. En el Portal de Azure, ve a tu
Function App > **Configuración** > **Variables de entorno** y añade:
```
FTP_HOST
FTP_USER
FTP_PASSWORD
FTP_FOLDER   (opcional)
FTP_USE_SSL  (opcional, "true" o "false")
```

## Obtener la Function Key (para llamarla desde AL)

Portal > tu Function App > la función `UploadToFtp` > **Claves de función**
> copia la key `default`. La URL completa a usar desde AL será del tipo:
```
https://<tu-function-app>.azurewebsites.net/api/UploadToFtp?code=<la-key>
```

## Formato del JSON esperado por la función

```json
{
  "fileName": "export.xml",
  "base64Content": "<contenido del XmlPort en Base64>"
}
```
