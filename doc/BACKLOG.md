# BACKLOG — Tero.Postino

**Versión:** `main` @ `d4eea3c` (2026-08-25)
**Fuente:** fusión de `backlog-purecode.md` + hallazgos de `POSTINO-CODE-REVIEW.md` (Analisis/CodeReview/).

---

## P0 — Crítico

_No hay ítems P0 abiertos._

---

## P1 — Importante

### [PO2-PAT-1] Path traversal en resolución de plantillas — Alta

`MailTemplateRenderer.ResolveTemplatePath` construye la ruta con `templateType` del
caller. El path resultante se usa directamente en `File.ReadAllTextAsync`. Un
`templateType` como `"../../etc/passwd"` lee cualquier archivo.

Se cerró `languageCode` con regex, pero `templateType` no tiene validación alguna.

**Fix:** validar `templateType` contra allowlist de tipos conocidos, o al menos
sanitizar para que no contenga `/` o `..`.

**Archivo:** `MailTemplateRenderer.cs`

### [PO2-QPR-1] FIFO única mezcla resets con recordatorios — Media

Una sola cola RabbitMQ (`postino.email.send`) transporta tanto tokens de reset de
contraseña (TTL en minutos) como recordatorios de turnos (masivos, batch diario).
Si el batch de recordatorios satura la cola, los resets quedan detrás.

**Fix:** colas separadas por prioridad (`postino.email.priority` + `postino.email.bulk`),
o al menos prefijo de mensaje que permita priorizar en el consumer.

### [PO2-HOL-1] Head-of-line blocking en consumer — Media

`MailBatchProcessor` hace `Task.Delay` dentro del handler de cada mensaje
(prefetch 1). Un email lento bloquea toda la cola.

**Fix:** mover el delay a después del ack, o usar `BasicConsumeAsync` sin
esperar entre mensajes.

---

## P2 — Mejora

### [PO2-JRN-1] Filename de bitácora se pisa en batches — Baja

`MailJournalService` escribe `{yyyyMMdd_HHmmss}_{to}.txt`. Si dos batches corren
en el segundo, el segundo pisa el primero.

**Fix:** agregar GUID o usar Append en vez de crear archivo nuevo.

### [PO2-IDP-1] Sin idempotencia de consumo de mensajes — Baja

Si el consumer procesa un mensaje y falla antes del ack, RabbitMQ reenvía. No hay
deduplication a nivel de aplicación.

**Fix:** agregar `MessageId` al publish y dedup en el consumer con tabla de mensajes procesados.

### [PO2-CFG-2] appsettings declara `RabbitMQ:*` pero código lee `Rabbit:*` — Baja

Configuración muerta: `appsettings.json` tiene `RabbitMQ:HostName` pero el código
usa `Rabbit:HostName`. El servicio funciona porque hay defaults, pero la config
real nunca se bindea.

**Fix:** alinear appsettings con el código o viceversa.

### [PO2-STL-1] Comentario stale en MailPublisher.cs:66 — Trivial

El comentario dice "por ahora no hay dead-letter" pero ya existe dead-letter.

**Fix:** actualizar o borrar el comentario.

### [PO3-HTTP-1] 503 cuando RabbitMQ no está disponible — RESUELTO ✅

`SendMailUseCase` ahora distingue `SendMailFailureKind.Validation` vs
`SendMailFailureKind.Infrastructure`. El controller devuelve 503 cuando RabbitMQ
no está disponible (reintentable), 400 cuando es validación.

### [PO3-DAT-1] Preservar datos y validar notificaciones — RESUELTO ✅

`SendMailUseCase` validación mejorada: campos requeridos verificados antes de encolar.

### [PO3-REM-1] Claims de recordatorios: completar o liberar — RESUELTO ✅

`SendAppointmentRemindersUseCase` ahora completa o libera claims de recordatorios
 correctamente, evitando que queden bloqueados.

### [PO3-I18N-1] Plantillas visuales en inglés y portugués — RESUELTO ✅

Plantillas HTML visuales completas en 3 idiomas (es/en/pt) para todas las
notificaciones: AppointmentBooked, Cancelled, Rescheduled, Reminder,
PasswordReset, EmailVerification, AdminCredentials.

---

## P3 — Menor

### [PO2-SIMP-1] ServiceIdentityValidator muerto — Trivial

Mismo ítem que AU2-DCO-1 y SH2-IDV-1. Borrar de Postino también.

---

## Resolución de backlog previo (PO-*)

| ID | Estado |
|----|--------|
| PO-P0-1 Retry SMTP | ✅ RESUELTO |
| PO-P0-2 Dead-letter | ✅ RESUELTO |
| PO-P0-3 Rate limiting envío | ✅ RESUELTO |
| PO-P1-1 Plantillas por idioma | ✅ RESUELTO |
| PO-P1-2 Recordatorios batch | ✅ RESUELTO |
| PO-P1-3 Bitácora de envíos | ✅ RESUELTO |
| PO-P1-4 Health check SMTP | ✅ RESUELTO |
| PO-P2-1 AuthenticatedHttpClientBase | ✅ RESUELTO |
| PO-P2-2 PlanStatusChecker | ✅ RESUELTO |
| PO-P2-3 Configuración multi-tenant | ✅ RESUELTO |
| PO-P3-* Menores | ✅ RESUELTOS |
