# CONTRATOS — Tero.Postino

**Versión:** `main` @ `d4eea3c` (2026-08-25)

## Endpoints expuestos

### Mail (`/api/mail`)

| Método | Path | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/mail/send` | Service token | Envío de email. Recibe `MailNotification` (polimórfico), resuelve plantilla, encola para envío SMTP. |

### Reminders (`/api/reminders`)

| Método | Path | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/reminders/batch` | Service token | Dispara batch de recordatorios pendientes para el día. |

### Health

| Método | Path | Auth | Descripción |
|--------|------|------|-------------|
| GET | `/health` | Anónimo | Health check del servicio |
| GET | `/alive` | Anónimo | Liveness probe |

## Endpoints consumidos

| Servicio | Endpoint | Uso |
|----------|----------|-----|
| Auth.Api | `POST /api/auth/service-token` | Obtener token de servicio (vía `AuthenticatedHttpClientBase`) |

## Mensajes RabbitMQ

### Cola: `postino.email.send`

| Campo | Tipo | Descripción |
|-------|------|-------------|
| Notification | `MailNotification` (JSON) | Notificación serializada (polimórfica) |
| TenantId | Guid | Tenant propietario |
| Priority | `MailPriority` | Normal, High, Low |

- **Exchange**: default (direct)
- **Dead-letter**: `postino.email.send.dead`
- **Prefetch**: configurable
- **Patrón**: publica `MailPublisher` → consume `MailBatchProcessor`

## Plantillas

Resolución por idioma + tipo de notificación:

```
templates/{LanguageCode}/{NotificationType}.html
```

Ejemplo: `templates/es/AppointmentBooked.html`

El `LanguageCode` viene del `MailNotification` (default `"es"`). Verificar que
todas las organizaciones propaguen el idioma correctamente (SH2-DFT-1).

## Errores

| Status | Significado |
|--------|-------------|
| 400 | Request inválido |
| 401 | Token de servicio inválido |
| 422 | Tenant sin configuración SMTP / plan inactivo |
| 500 | Error interno (SMTP caído, plantilla no encontrada) |
