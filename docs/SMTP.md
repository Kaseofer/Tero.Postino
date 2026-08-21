# Configurar SMTP (envío real de mails)

Sin esto configurado, `MailQueueConsumer` loguea cada mensaje y no manda nada — es el
comportamiento por default, para no romper en un ambiente sin credenciales. Esta guía es para
cuando sí querés que salgan de verdad.

## Dos servicios, dos configuraciones

Ojo: **hay que cargar las credenciales en los dos lugares**, y **las claves no se llaman
igual** en cada uno (herencia de cómo se armó cada proyecto — no las renombramos para no romper
configuración ya existente):

| | `Tero.Postino` | `Tero.Auth.Api` |
|---|---|---|
| Qué manda | La cola (`postino.mail.queue`) — todo lo que Auth/Appointments/Gateway encolan | Directo, sin pasar por la cola (registro, reset de contraseña) |
| Sección | `Smtp` | `Smtp` |
| Remitente | `Smtp:FromAddress` / `Smtp:FromName` | `Smtp:From` / `Smtp:FromDisplayName` |
| Resto de las claves | `Smtp:Host` / `Smtp:Port` / `Smtp:Username` / `Smtp:Password` | igual |

Mismo host/usuario/contraseña de Gmail sirve para los dos — sólo cambia cómo se llama la clave
del remitente.

## Nunca en `appsettings.json`

Las credenciales son secretos: van por **user-secrets** (recomendado para desarrollo local) o
**variables de entorno** (recomendado para Docker/CI). El código lee de cualquiera de los dos
indistintamente — es la configuración estándar de .NET (`IConfiguration`), no algo específico
de Postino.

## Opción A — user-secrets

Parado en la carpeta del proyecto `Api` correspondiente:

```bash
cd src/Tero.Postino.Api
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Smtp:Port" "587"
dotnet user-secrets set "Smtp:Username" "tu-cuenta@gmail.com"
dotnet user-secrets set "Smtp:Password" "TU_APP_PASSWORD_16_CARACTERES"
dotnet user-secrets set "Smtp:FromAddress" "tu-cuenta@gmail.com"
dotnet user-secrets set "Smtp:FromName" "Tero"
```

Para `Tero.Auth.Api`, mismo comando parado en `src/Tero.Auth.Api`, cambiando `FromAddress`/
`FromName` por `From`/`FromDisplayName`.

## Opción B — variables de entorno (PowerShell)

.NET arma las secciones anidadas con `__` (doble guión bajo) en el nombre de la variable:

```powershell
[System.Environment]::SetEnvironmentVariable("Smtp__Host", "smtp.gmail.com", "User")
[System.Environment]::SetEnvironmentVariable("Smtp__Port", "587", "User")
[System.Environment]::SetEnvironmentVariable("Smtp__Username", "tu-cuenta@gmail.com", "User")
[System.Environment]::SetEnvironmentVariable("Smtp__Password", "TU_APP_PASSWORD_16_CARACTERES", "User")
[System.Environment]::SetEnvironmentVariable("Smtp__FromAddress", "tu-cuenta@gmail.com", "User")
[System.Environment]::SetEnvironmentVariable("Smtp__FromName", "Tero", "User")
```

`"User"` las persiste para tu usuario de Windows entre reinicios, sin ser variables globales de
todo el sistema — punto medio razonable para una máquina de desarrollo compartida.

**Importante:** `SetEnvironmentVariable` escribe en el registro. Un proceso que ya estaba
corriendo (o una terminal ya abierta) **no las ve** hasta que arranca de nuevo — el bloque de
entorno se hereda del proceso padre al nacer, no se relee después. Si acabás de cargarlas,
cerrá y volvé a abrir la terminal (o la IDE) antes de correr el servicio.

## Opción C — Docker / docker-compose

Mismo esquema de nombres, como variables de entorno del contenedor:

```yaml
services:
  postino-api:
    environment:
      - Smtp__Host=smtp.gmail.com
      - Smtp__Port=587
      - Smtp__Username=tu-cuenta@gmail.com
      - Smtp__Password=${SMTP_PASSWORD}   # desde un .env, nunca hardcodeado en el yaml versionado
      - Smtp__FromAddress=tu-cuenta@gmail.com
      - Smtp__FromName=Tero
```

`${SMTP_PASSWORD}` se resuelve desde un archivo `.env` (que va en `.gitignore`, nunca
versionado) o desde el secreto que gestione tu orquestador (Docker secrets, variables de CI,
etc.) — el `docker-compose.yml` en sí puede versionarse porque no lleva el valor real adentro.

## Gmail específicamente

Google **no permite** autenticar SMTP con la contraseña normal de la cuenta. Hace falta una
**App Password**: una credencial de 16 caracteres, distinta de tu contraseña, generada
específicamente para esto.

### Requisito: verificación en 2 pasos

Las App Passwords sólo existen si la cuenta tiene 2FA activado. Chequealo en
[myaccount.google.com/security](https://myaccount.google.com/security) — si dice "Desactivada",
activala primero (con el celular, es rápido). No hay forma de generar una App Password sin
este paso.

### Generarla

1. [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords) (si el link
   no entra directo, buscá "Contraseñas de aplicaciones" desde la página de seguridad de la
   cuenta).
2. Ponele un nombre (ej. "Tero SMTP") y generá.
3. Google te da 16 caracteres, generalmente mostrados en 4 grupos de 4 con espacios
   (`abcd efgh ijkl mnop`).

### Al copiarla, dos errores comunes (nos pasaron los dos hoy)

- **Copiar con espacios de más, o un carácter de sobra.** `Smtp:Password` tiene que tener
  **exactamente 16 caracteres**. Si tiene 17 o 15, algo se coló o se perdió al copiar — no lo
  edites a mano adivinando cuál sobra: volvé a la página de Google y copiala de nuevo, entera.
- **Cargar la contraseña pero olvidarse del remitente.** `Smtp:FromAddress` (Postino) o
  `Smtp:From` (Auth) es una variable aparte — si falta, el servicio loguea "SMTP no
  configurado" y no intenta mandar, aunque el resto esté bien.

### Verificar que quedó bien

Contá los caracteres antes de darlo por hecho:

```powershell
$pw = [System.Environment]::GetEnvironmentVariable("Smtp__Password", "User")
"Longitud: $($pw.Length)"   # tiene que decir 16
```

Si el envío real falla con `5.7.0 Authentication Required`, es casi siempre esto: contraseña
normal en vez de App Password, o App Password mal copiada.
