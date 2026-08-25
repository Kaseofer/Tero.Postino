# ARQUITECTURA — Tero.Postino

**Versión:** `main` @ `d4eea3c` (2026-08-25)

## Resumen

Servicio de notificaciones: recibe pedidos de envío (vía HTTP o RabbitMQ), resuelve
plantillas, y entrega por email (SMTP) o WhatsApp (vía Gateway). Multi-tenant:
cada organización tiene sus propias credenciales SMTP y configuración de plantillas.

## Componentes principales

### Controllers

| Controller | Ruta | Auth | Qué hace |
|-----------|------|------|----------|
| `MailController` | `/api/mail/*` | Service token (`client_id`) | Envío de emails (POST-01): recibe `MailNotification`, resuelve plantilla, envía por SMTP |
| `RemindersController` | `/api/reminders/*` | Service token | Disparador de recordatorios: batch de recordatorios pendientes para el día |

### Use Cases / Hosted Services

| Componente | Responsabilidad |
|-----------|----------------|
| `MailPublisher` | Publica mensajes de email en RabbitMQ (cola `postino.email.send`) |
| `MailTemplateRenderer` | Resuelve plantilla (idioma + tipo de notificación) y renderiza HTML |
| `MailBatchProcessor` | BackgroundService: consume la cola de emails y envía por SMTP |
| `ReminderBatchProcessor` | BackgroundService: procesa recordatorios pendientes y envía por SMTP/WhatsApp |
| `MailJournalService` | BackgroundService: retención de bitácora de envíos |

### Infraestructura

| Componente | Rol |
|-----------|-----|
| `SmtpClient` (MailKit) | Envío real por SMTP |
| `AuthenticatedHttpClientBase` | Cliente HTTP base con token de servicio (patrón que Gateway extrajo primero) |
| `PlanStatusChecker` | Verifica estado del plan del tenant antes de enviar |

## Modelo de dominio

- **MailJob**: trabajo de envío pendiente (notification serializada, destinatario, estado)
- **MailJournal**: registro de envío (para, asunto, estado, timestamp) — retención configurable
- **ReminderRecord**: recordatorio pendiente (asociado a un turno)

## Seguridad

- Service token para endpoints HTTP (`client_id` claim)
- Rate limiting en envío de emails (previene abuso)
- Verificación de plan del tenant antes de enviar

## Flujo principal (email)

```
Otro servicio → POST /api/mail/send → MailPublisher → RabbitMQ → MailBatchProcessor → SMTP → Destinatario
```

## Flujo principal (recordatorio)

```
RemindersController → ReminderBatchProcessor → Plantilla → SMTP o WhatsApp Gateway
```

## Dependencias externas

- **Postgres**: mail jobs, journals, reminder records
- **RabbitMQ**: cola `postino.email.send` (y dead-letter)
- **SMTP**: servidor de correo configurado por tenant
- **Auth API**: obtiene tokens de servicio
- **WhatsApp Gateway**: envío de recordatorios por WhatsApp (opcional)
