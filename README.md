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

## Qué falta

Lo escribo explícito porque es la mitad de abajo del diagrama:

- **El worker no existe.** No hay nada que consuma la cola.
- **No hay envío real.** Ninguna integración con un servidor de correo: hoy un pedido se acepta,
  se encola, y ahí queda.
- **No hay reintentos ni dead-letter**, que es justamente lo que justifica que haya una cola.
- **No hay plantillas.** El diseño dice que Postino arma el mail; todavía no hay con qué.

  La dirección prevista: las plantillas viven **como archivos `.html` en este repositorio**, en
  una carpeta propia, para poder abrirlas con doble clic y ver cómo quedan sin levantar nada.
  El pedido elige cuál usar y aporta los datos; Postino la rinde. Editar el diseño de un mail
  pasa a ser editar un archivo y verlo en el navegador.

## Autenticación entre servicios

Hoy valida dos headers (`X-Tero-Service-Id` y `X-Tero-Service-Token`) contra una lista en
configuración.

**Es distinto a lo que hace el resto del sistema**, que usa los tokens de servicio que emite
Auth: JWT de vida corta que además llevan la organización. El token estático no la lleva, y las
notificaciones de turno son de una organización concreta — hoy no hay forma de atribuirlas.
Queda pendiente unificarlo.

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
