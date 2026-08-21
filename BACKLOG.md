# Backlog — Tero.Postino

**Resueltos: #1, #2, #3, #5, #6, #9 (21/08), #7, #8 (21/08).** Ver detalle al pie de cada
ítem. Quedan abiertos: #4 (necesita coordinación con Auth); #10 (tests — el usuario los
agrega él mismo, no se escriben acá) y #11 (Baja, sólo si se justifica).

Levantado del análisis del sistema de plantillas de notificaciones (post-migración a
`Templates/{idioma}/{Tipo}.html`, commits `00404ec` y `7fd9279`). Ordenado por prioridad:
el criterio fue impacto en el usuario final primero, robustez después, higiene de código al final.

---

## Alta — afectan al usuario final

---

### 1. Asuntos hardcodeados en español aunque el cuerpo esté en otro idioma — ✅ RESUELTO

**Problema detectado**

La localización se implementó sólo para los cuerpos: cada carpeta `Templates/{idioma}/` tiene
sus `.html` traducidos, pero el asunto del mail lo arma `SendMailUseCase.BuildContent()` con
textos fijos en español, sin importar el idioma pedido:

- `"Restablece tu contraseña"` (`SendMailUseCase.cs:89`)
- `"Verifica tu correo electrónico"` (`SendMailUseCase.cs:99`)
- `"Credenciales administrador para …"` (`SendMailUseCase.cs:108`)
- prefijos de turno `"Confirmación"/"Cancelación"/…` (`SendMailUseCase.cs:124-131`)

**Ejemplo concreto:** un contacto con `languageCode=en` que pide reset de contraseña recibe:

> **Subject:** Restablece tu contraseña
> Body: *Hello John, we received a request to reset your password…*

**Impacto:** el primer dato que ve el usuario (el asunto, en la bandeja de entrada) queda en
un idioma que no entiende. La i18n quedó a medias justo en la parte más visible.

**Solución propuesta**

Mover el asunto al mismo mecanismo de convención que ya resuelve el cuerpo, así
`SendMailUseCase` deja de conocer textos:

1. Opción mínima: archivo de una línea por tipo+idioma — `Templates/{idioma}/{Tipo}.subject.txt`
   — con placeholders (`{{serviceName}}`). El renderer agrega un método
   `RenderSubject(templateType, languageCode, model)` que lo lee y sustituye.
2. Opción más rica: sección `<title>` dentro del propio `.html`; el renderer la extrae antes
   de strippearla del cuerpo y la devuelve junto al HTML.
3. `MailQueueConsumer` usa ese asunto al armar el mail saliente en vez del que trae el DTO.

En ambos casos agregar el archivo a `ValidateTemplatesExistOrThrow()` para que falle al
arranque si falta algún asunto.

---

### 2. Sin fallback de idioma — ✅ RESUELTO

**Problema detectado**

Si llega un `languageCode` que no tiene carpeta (ej. `"fr"`, o `"es-AR"` cuando sólo existe
`es`), `ResolveTemplatePath()` arma `Templates/fr/{tipo}.html`, el archivo no existe y el mail
cae directo al HTML genérico crudo (lista clave/valor) — aunque sí exista la plantilla del tipo
en español.

- Ref: `src/Tero.Postino.Infrastructure/Email/MailTemplateRenderer.cs:105-114`

**Ejemplo concreto:** request válido de verificación con `languageCode="fr"` produce:

```html
<div style="font-family: sans-serif;"><p><strong>userName:</strong> John</p>
<p><strong>verificationUrl:</strong> https://…</p><p><strong>priority:</strong> Normal</p></div>
```

**Impacto:** un idioma no soportado degrada el mail a algo ilegible para el usuario final,
cuando bastaría mandarle la versión en español (idioma por defecto del sistema).

**Solución propuesta**

1. En `ResolveTemplatePath()`: si `Templates/{idioma}/{tipo}.html` no existe pero sí existe
   `Templates/{DefaultLanguageCode}/{tipo}.html`, devolver esa ruta.
2. Loggear warning con el idioma pedido (dato útil para decidir qué traducciones sumar).
3. El genérico queda sólo como última red si tampoco hay archivo en el default (ya cubierto
   por `ValidateTemplatesExistOrThrow()`, así que en la práctica nunca debería pasar).
4. Complementar con #6 (validar formato del código de idioma) para cerrar el tema.

---

### 3. Valores interpolados sin codificar HTML — ✅ RESUELTO

**Problema detectado**

`RenderTemplate()` inserta cada valor con `value.ToString()` crudo, y `RenderGeneric()` hace lo
mismo con clave y valor. Ningún dato que llega en el `TemplateModel` se codifica antes de
meterse en el HTML.

- Ref: `src/Tero.Postino.Infrastructure/Email/MailTemplateRenderer.cs:127-130` y `135-139`

**Ejemplo concreto 1 (texto):** un servicio creado por un tenant llamado
`<img src=x onerror=alert(1)>Consultas` viaja como `serviceName` y el mail contiene esa etiqueta
ejecutable en clientes que permiten HTML activo.

**Ejemplo concreto 2 (atributo):** las plantillas interpolan dentro de `href`:

```html
<a href="{{verificationUrl}}">Verify my email</a>
```

Un `verificationUrl` con `"` rompe/sobreescribe el atributo. Hoy los emisores son microservicios
internos confiables, pero Postino no valida ni codifica nada en su frontera.

**Agravante actual:** con el nuevo tipo `AdminCredentials` se mandan contraseñas por mail (#4),
así que el canal ya transporta datos sensibles y merece tratamiento defensivo.

**Impacto:** inyección de HTML/JS en emails (phishing, defacement de la marca, robo de clicks)
si cualquier emisor o dato de tenant se contamina.

**Solución propuesta**

1. En `RenderTemplate()`: `HtmlEncode(value)` en toda sustitución de texto plano.
2. Para URLs (`verificationUrl`, `resetUrl`): construirlas con `Uri.EscapeDataString` sobre el
   token (ver #5) y validar esquema `https`.
3. En `RenderGeneric()`: encodear también las claves.
4. Si algún día hace falta interpolar HTML real, agregar placeholder explícito `{{clave|raw}}`
   — nunca dejar el default sin codificar.

---

## Media — robustez y seguridad

---

### 4. `AdminCredentials` manda contraseña en texto plano por email

**Problema detectado**

El nuevo tipo de notificación incluye la contraseña literal del admin en el cuerpo:

```html
<p><strong>Contraseña:</strong> {{password}}</p>
```

- Ref: `src/Tero.Postino.Infrastructure/Templates/*/AdminCredentials.html`,
  `SendMailUseCase.cs:107-114`

El mail queda copiado en bandeja, backups del proveedor e historial del destinatario para
siempre. Cualquier filtración posterior de esa casilla expone una credencial activa del panel
de administración del tenant.

**Impacto:** vector de compromiso de cuentas administradoras; incumple prácticas estándar
(OWASP: no enviar secretos permanentes por canales no cifrados de extremo a extremo).

**Solución propuesta**

1. Preferente: replicar el mecanismo de PasswordReset — enviar link de
   *"definí tu contraseña"* con token de un solo uso y expiración; la plantilla deja de tener
   `{{password}}`. Requiere soporte del equipo de Auth para generar ese token.
2. Mínimo aceptable (si se mantiene la contraseña inicial): garantizar política de cambio
   obligatorio en el primer login, acortar la vigencia de esa contraseña semilla, y documentar
   la decisión de riesgo acá mismo y en el README.

---

### 5. Construcción frágil de URLs con token — ✅ RESUELTO

**Problema detectado**

Los tokens se concatenan a mano asumiendo que la URL base no tiene query string y sin
encodearlos:

```csharp
{ "resetUrl", $"{n.ActionUrl}?token={n.Token}" },          // SendMailUseCase.cs:93
{ "verificationUrl", $"{n.ActionUrl}?token={n.Token}" },   // SendMailUseCase.cs:103
```

**Ejemplo concreto:** un `ActionUrl = "https://app.tero.com/reset?lang=en"` produce
`…reset?lang=en?token=abc` — URL rota, usuario bloqueado. Un token con `+` o `=` (típico en
base64) puede truncarse o corromperse al decodificarse del otro lado.

**Impacto:** links de reset/verificación rotos = usuarios que no pueden entrar ni validar su
cuenta; es de los peores mails para que falle porque bloquea el acceso.

**Solución propuesta**

1. Helper único `BuildActionUrl(string baseUrl, string token)` usando `UriBuilder` (maneja el
   `?` vs `&` correctamente) + `Uri.EscapeDataString(token)`.
2. Usarlo desde los dos casos de arriba.
3. Unit tests: base sin query, base con query, token con caracteres especiales.

---

### 6. `LanguageCode` libre llega directo a `Path.Combine` — ✅ RESUELTO

**Problema detectado**

El `languageCode` del request no se valida: viaja por la cola y termina compuesto en la ruta
del archivo de plantilla:

```csharp
return Path.Combine(_templatesDirectory, language, $"{templateType}.html");  // :113
```

Un valor tipo `..\..\algo` apunta fuera de `Templates/`. El daño real es bajo — sólo se leen
archivos `.html`, `templateType` sale de un enum y hace falta JWT de servicio — pero es una
puerta innecesaria.

**Impacto:** lectura limitada de archivos fuera del directorio esperado por parte de un
servicio comprometido; ruido de paths impredecibles en logs.

**Solución propuesta**

1. Validar el formato en `ResolveTemplatePath()`: regex `^[a-z]{2}(-[A-Z]{2})?$` o whitelist
   contra `Directory.GetDirectories(_templatesDirectory)` (que ya se lista en
   `ValidateTemplatesExistOrThrow`).
2. Valor inválido → usar default `es` + warning (combinable con el fallback del ítem #2).

---

### 7. Mails que fallan se descartan sin dead-letter — ✅ RESUELTO

**Problema detectado**

Ante un error de render o envío SMTP, el consumidor hace `nack(requeue:false)` y el mensaje se
pierde sin ningún registro accionable más que el log puntual. No hay reintento ni cola de
muertos. Es una limitación reconocida en el README ("no hay reintentos ni dead-letter").

- Ref: `src/Tero.Postino.Infrastructure/RabbitMq/MailQueueConsumer.cs`

**Impacto:** un error transitorio de SMTP (timeout, throttling de Gmail) pierde mails para
siempre — recordatorios de turnos y resets de contraseña son justamente mails sensibles al
tiempo.

**Solución propuesta**

1. Declarar cola `postino.mail.dead` (y binding al exchange existente).
2. Reintento con backoff: hasta 3 intentos (delay creciente) antes de dead-letter; puede ser
   simple republish con header `x-retry-count` o cola de delay.
3. Al dead-letter: publicar motivo del error + payload original completo para reprocesar.
4. Exponer contador de dead-lettered en la observabilidad ya existente (Seq/OBSERVABILITY.md).

---

### 8. `PlainTextBody` muerto: se envía siempre HTML-only — ✅ RESUELTO

**Problema detectado**

El contrato acepta `PlainTextBody`, pero el consumidor nunca lo renderiza y `SmtpMailSender`
manda siempre `IsBodyHtml = true` sin parte alternativa multipart. Resultado: todo mail sale
sólo-HTML.

- Ref: `src/Tero.Postino.Infrastructure/RabbitMq/MailQueueConsumer.cs`,
  `src/Tero.Postino.Infrastructure/Email/SmtpMailSender.cs`

**Impacto:** lectores de pantalla y clientes que prefieren texto plano pierden contenido;
varios filtros antispam penalizan HTML sin parte `text/plain`.

**Solución propuesta**

1. Cuando el mensaje venga con plantilla, generar el texto plano automáticamente: strip de
   tags del HTML renderizado + colapso de espacios + links en línea (`texto (url)`).
2. Enviar multipart/alternative vía `AlternateView` en `SmtpMailSender`.
3. Respetar `PlainTextBody` explícito si el emisor lo mandó (hoy campo muerto).

---

## Baja — higiene y calidad

---

### 9. Datos fantasma en el modelo de turno — ✅ RESUELTO

**Problema detectado**

`AppointmentModel()` mete en el modelo `priority` y `durationMinutes`, pero ninguna plantilla
los consume. Viajan serializados por la cola en cada notificación y aparecen como ruido si el
mail cae al genérico.

- Ref: `src/Tero.Postino.Application/Email/UseCases/SendMailUseCase.cs:133-154`

**Solución propuesta:** decidir y ejecutar uno de los dos caminos —
1. usarlos: mostrar duración en el cuerpo (`::optional:durationMinutes::`) es razonable y
   barato; o
2. sacarlos del modelo si nadie los va a mostrar.

No dejarlos viajando "por las dudas".

---

### 10. Proyecto de tests vacío

**Problema detectado**

`tests/Tero.Postino.Api.Tests` existe pero no tiene ni un test. El código más testeable del
servicio — puro, sin infraestructura — es justamente el renderer y su validación de arranque:
regex de `::optional::`, strip de comentarios, resolución por convención, fallbacks,
`ValidateTemplatesExistOrThrow`.

**Solución propuesta:** cubrir primero, en este orden —

1. `RenderTemplate`: placeholder presente/faltante, bloque opcional vacío/presente,
   comentarios eliminados del output.
2. `Render` end-to-end contra carpeta temporal: convención correcta, idioma default, fallback.
3. `ValidateTemplatesExistOrThrow`: falla cuando falta un archivo, pasa con las 21 combinaciones.
4. Helper de URLs del ítem #5.

Son todos unit tests rápidos; después de implementar los ítems 1-6 este archivo debería crecer
en paralelo.

---

### 11. Calidad del HTML de email

**Problema detectado**

Las plantillas son fragmentos: sin `<html>/<body>/lang`, sin preheader, sin estructura de
tablas (Outlook clásico), estilos sólo inline. Funciona, pero se ve pobre y no hay lugar único
para estilos comunes entre las 21 plantillas.

**Solución propuesta (sólo si el volumen/marca lo justifica)**

1. Layout compartido por idioma — `Templates/{idioma}/_layout.html` con `<html lang>`,
   preheader y estilos base — que envuelva el cuerpo por convención (coherente con el diseño
   actual de resolver archivos por nombre).
2. El renderer compone `layout` + cuerpo (reemplazando un marker `{{body}}`).
3. Mantener los cuerpos actuales como contenido del layout — edición simple intacta.

---

## Resumen

| # | Tema | Prioridad | Esfuerzo estimado | Estado |
|---|---|---|---|---|
| 1 | Asuntos no localizados | Alta | M | ✅ Resuelto |
| 2 | Fallback de idioma | Alta | S | ✅ Resuelto |
| 3 | HtmlEncode en sustituciones | Alta | S | ✅ Resuelto |
| 4 | Contraseña plana en AdminCredentials | Media | M-L | Pendiente (coordinación con Auth) |
| 5 | Armado de URLs con token | Media | S | ✅ Resuelto |
| 6 | Validar `LanguageCode` | Media | S | ✅ Resuelto |
| 7 | Dead-letter queue | Media | L | ✅ Resuelto |
| 8 | Parte texto plano (multipart) | Media | M | ✅ Resuelto |
| 9 | Datos fantasma (`priority`, `durationMinutes`) | Baja | S | ✅ Resuelto |
| 10 | Tests del renderer | Baja | M | Pendiente (los agrega el usuario) |
| 11 | Layout HTML compartido | Baja | M | Pendiente (sólo si se justifica) |

*Leyenda esfuerzo: S < medio día · M ~ 1 día · L > 1 día*
