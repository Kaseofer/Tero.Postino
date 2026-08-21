# Tero.Postino

El servicio de correo de Tero. **Es el único que habla con la cola de mails**: los demás
servicios le piden un envío por HTTP y se olvidan.

## La idea

Mandar un mail es lento y falla seguido —el servidor de correo no contesta, rechaza, tarda—, y
ninguno de esos problemas debería frenar a quien pidió el envío. Cuando alguien se registra, la
respuesta tiene que llegar enseguida; que el mail de verificación salga es otra historia, que
puede tardar y reintentarse.

Postino separa esas dos historias:

```
Auth / Appointments / Gateway
        │  POST api/email/...   (qué mail quiero, con qué datos)
        ▼
     Postino.Api  ──────►  RabbitMQ  ──────►  Worker  ──────►  servidor de correo
       (acepta)             (buzón)          (entrega y reintenta)
```

Quien pide el envío recibe una respuesta apenas el pedido queda encolado. A partir de ahí es
problema de Postino, y tiene todo el tiempo del mundo para resolverlo.

## Dos decisiones que definen el diseño

**Nadie más toca la cola.** Los otros servicios no publican mensajes: hacen un `POST`. Así hay
un solo dueño de la topología (exchange, routing keys, reintentos, dead-letter), y el mensaje
que viaja por la cola es asunto interno de Postino — puede cambiar sin coordinar con nadie.

La alternativa —que cada servicio publique a la cola— obliga a que todos conozcan el mismo
contrato de mensaje y lo interpreten igual. Cuando eso se desincroniza no da error: el mail
llega incompleto y nadie se entera.

**El que pide manda datos, no HTML.** Un `ResetPasswordRequest` lleva el destinatario, el
nombre, el token y la URL base — no un cuerpo ya armado. Cómo se ve un mail de reset lo decide
Postino, en un solo lugar, y se puede cambiar sin tocar Auth.

## Qué hay hoy

Tres endpoints, uno por tipo de mail:

| Endpoint | Para qué |
|---|---|
| `POST api/email/verify-email` | Verificación de dirección de correo |
| `POST api/email/reset-password` | Recuperación de contraseña |
| `POST api/email/appointment-notification` | Aviso de turno |

Los contratos (`...Request` y `...Response`) viven en el paquete `Tero.Contracts`, para que
quien llama los tenga tipados sin depender de este repo.

Cuatro capas, con la dirección de dependencias enforceada por el compilador: `Domain` no conoce
a nadie, `Application` tiene los casos de uso y los puertos, `Infrastructure` las
implementaciones, y `Api` compone.

## El worker (`MailQueueConsumer`)

Existe desde POST-01: un `BackgroundService` en `Tero.Postino.Infrastructure/RabbitMq/` consume
`postino.mail.queue`, resuelve el HTML (directo si el mensaje trae `HtmlBody`, o renderizado
si trae `TemplateType` + `TemplateModel`) y manda por SMTP con `SmtpMailSender`
(`System.Net.Mail.SmtpClient`, sin dependencia externa — mismo mecanismo que
`Tero.Auth.Api.SmtpEmailSender`).

**Plantillas como archivos `.html`**, tal como decía este README antes de que existiera nada de
esto: viven en `Tero.Postino.Infrastructure/Templates/`, se abren con doble clic. El motor de
reemplazo es deliberadamente simple (`MailTemplateRenderer`): `{{clave}}` se sustituye por texto
plano, y `::optional:clave::...::/optional::` hace desaparecer un tramo entero si esa clave no
vino en el modelo. No hay condicionales anidados ni loops — el día que un mail los necesite,
ahí sí se justifica sumar un motor de verdad (Handlebars, Scriban).

Ver [`docs/SMTP.md`](docs/SMTP.md) para configurar las credenciales.

## Qué falta

- **No hay reintentos ni dead-letter.** Un mensaje que falla al mandar se descarta
  (`nack` sin reintentar) en vez de girar para siempre o caer a una cola separada para
  revisar a mano — es la primera limitación real a resolver si el volumen crece.
- **Plantillas: sólo `AppointmentNotification`.** Un `TemplateType` que no tenga archivo cae a
  un HTML genérico armado en C# (lista de pares clave/valor) en vez de fallar.

## Autenticación entre servicios

JWT de servicio emitido por Auth (`POST api/auth/service-token`) — mismo mecanismo que
Appointments y el Gateway. `EmailController` exige `[Authorize]` más el claim `client_id`
(sólo presente en tokens de servicio, nunca en uno de usuario final).

Reemplaza el mecanismo anterior (dos headers estáticos, `X-Tero-Service-Id` y
`X-Tero-Service-Token`, contra una lista en configuración) que este README marcaba como
pendiente de unificar: ese token no llevaba la organización, y las notificaciones de turno son
de una organización concreta — no había forma de atribuirlas. El JWT sí la lleva
(`tenant_id`).

Para que un llamador (Auth, Appointments, el Gateway) pueda pedir un token para hablarle a
Postino, hace falta un `ServiceClient` sembrado del lado de Auth con `ClientId = "postino"` —
hoy sólo existe el de `whatsapp-gateway`.

## Correrlo

Forma parte del entorno que levanta el AppHost del repositorio que orquesta el sistema, junto
con RabbitMQ, Postgres y los demás servicios. También se puede correr solo:

```
dotnet run --project src/Tero.Postino.Api
```

Necesita credenciales para restaurar los paquetes `Tero.*` desde GitHub Packages: las variables
de entorno `GITHUB_PACKAGES_USER` y `GITHUB_PACKAGES_TOKEN` (ver `nuget.config`).

Las credenciales de SMTP y de RabbitMQ van por user-secrets o variables de entorno, nunca en
`appsettings.json`.
