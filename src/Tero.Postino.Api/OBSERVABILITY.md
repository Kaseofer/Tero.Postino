# 📊 Observabilidad en Tero.Postino

## Descripción General

Tero.Postino está completamente integrado en la infraestructura de observabilidad del ecosistema Tero. Todos los logs, trazas (traces) y métricas se centralizan en **Seq** a través de **OpenTelemetry**.

## Arquitectura

```
┌─────────────────────────────────┐
│ Tero.Postino                    │
│ - Application Logs              │
│ - Distributed Traces (OTEL)     │
│ - Metrics (OTEL)                │
└────────────────┬────────────────┘
                 │
     ┌───────────┴──────────┬────────────────┐
     │                      │                │
     ▼                      ▼                ▼
┌─────────────┐   ┌──────────────┐   ┌─────────────┐
│ Aspire      │   │ OpenTelemetry│   │ ServiceDef- │
│ AppHost     │   │ OTLP Export  │   │ aults       │
│ (Seq ref)   │   │ (HTTP+Protob)│   │ (Logging)   │
└─────────────┘   └──────────────┘   └─────────────┘
     │                  │                  │
     └──────────────────┼──────────────────┘
                        │
                        ▼
          ┌──────────────────────────┐
          │ Seq Server               │
          │ :5341 (Web UI)           │
          │ :5341/ingest/otlp (OTLP) │
          └──────────────────────────┘
```

## Componentes

### 1. AddServiceDefaults() 
**Ubicación**: `src/shared/Tero.ServiceDefaults/Extensions.cs`

Registra automáticamente:
- **OpenTelemetry con OTLP Exporter** hacia Seq
- **ILogger** configurado para escribir estructurado
- **Health Checks** con observabilidad

```csharp
builder.AddServiceDefaults(); // En Program.cs línea 12
```

### 2. OpenTelemetry OTLP Configuration

Postino envía 3 tipos de señales a Seq:

| Señal | Endpoint | Protocolo | Descripción |
|-------|----------|-----------|-------------|
| **Logs** | `{seq}/ingest/otlp/v1/logs` | HTTP/Protobuf | Eventos y debugging |
| **Traces** | `{seq}/ingest/otlp/v1/traces` | HTTP/Protobuf | Distributed tracing |
| **Metrics** | Solo a Aspire Dashboard | OpenTelemetry Collector | CPU, memoria, requests |

### 3. AppHost Integration

**Ubicación**: `src/Tero.AppHost/AppHost.cs:51-58`

```csharp
var postino = builder.AddProject<Projects.Tero_Postino>("postino")
    .WithReference(rabbitMq)      // Acceso a RabbitMQ
    .WithReference(postgresDb)    // Acceso a PostgreSQL (futuro)
    .WithReference(seq)           // ← Connection string de Seq inyectada aquí
    .WithEnvironment("HealthChecks__ApiKey", healthApiKey)
    .WaitFor(rabbitMq)
    .WaitFor(postgresDb)
    .WaitFor(seq);                // ← Espera a que Seq esté listo
```

El `.WithReference(seq)` automáticamente:
- Inyecta `ConnectionStrings__seq` en appsettings
- La configura como variable de entorno
- Se usa en `Tero.ServiceDefaults` para el OTLP Exporter

## Logs Enviados a Seq

### Niveles de Log

| Nivel | Fuente | Ejemplo |
|-------|--------|---------|
| `Information` | TeroServiceAuthenticationFilter | "Servicio auth-api validado correctamente" |
| `Warning` | TeroServiceAuthenticationFilter | "Intento de acceso sin header X-Tero-Service-Id" |
| `Error` | Use Cases | "Error al enviar email a usuario@example.com" |
| `Debug` | RabbitMQ Publisher | "Mensaje encolado: mail-send-verification-email-{id}" |

### Estructura de Logs (Structured Logging)

Cada log incluye automáticamente:

```json
{
  "Timestamp": "2024-12-20T10:30:45.1234567Z",
  "Level": "Information",
  "MessageTemplate": "Servicio {ServiceId} validado correctamente",
  "ServiceId": "auth-api",
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SpanId": "d7f17c6b1f7e4e1a",
  "Host": "postino",
  "Environment": "Development"
}
```

## Acceso a Seq

### En Desarrollo (via Aspire)

```bash
cd src/Tero.AppHost
dotnet run
# Aspire Dashboard: http://localhost:15001
# Click en "Seq" → http://localhost:5341
```

### Dashboard de Seq

1. **Home Page**: Overview de logs recientes
2. **Data Explorer**: Búsqueda avanzada de logs
3. **Signals**: Traces distribuidos
4. **Settings**: Configuración de alertas y retención

### Búsquedas Útiles en Seq

```sql
-- Todos los logs de Postino
Host = "postino"

-- Solo aceptación de requests autenticados
Host = "postino" AND MessageTemplate LIKE "%validado correctamente%"

-- Fallos de autenticación
Host = "postino" AND Level = "Warning"

-- Emails encolados (en infraestructura)
Host = "postino" AND MessageTemplate LIKE "%encolado%"

-- Trazas distribuidas completas (desde Auth → Postino → RabbitMQ)
MessageTemplate LIKE "%verify-email%"
```

## Trace Distribuido Ejemplo

Cuando Auth.Api llama a Postino para enviar email de verificación:

```
┌─────────────────────────────────────────────────────┐
│ Auth.Api - POST /api/auth/register                  │
│ TraceId: 4bf92f3577b34da6a3ce929d0e0e4736           │
└──────────────┬──────────────────────────────────────┘
               │
               ▼
    ┌──────────────────────────────┐
    │ Postino - POST /api/email    │
    │ TraceId: [same]              │
    │ SpanId: d7f17c6b1f7e4e1a     │
    └──────────┬───────────────────┘
               │
               ▼
    ┌──────────────────────────────┐
    │ RabbitMQ - Publish Event     │
    │ TraceId: [same]              │
    │ SpanId: a8d92e5c3j2k9f4b     │
    └──────────────────────────────┘

Todo con mismo TraceId → Visible en Seq como trace único
```

## Monitoreo

### Alertas Recomendadas en Seq

1. **Autenticación Fallida**
   ```sql
   Host = "postino" AND MessageTemplate = "Servicio no autorizado: {ServiceId}"
   ```
   → Dispara alerta si cuenta > 5 en 5 minutos

2. **Errores en Envío de Email**
   ```sql
   Host = "postino" AND Level = "Error"
   ```
   → Dispara alerta si count > 0

2b. **Mails en dead-letter** (`postino.mail.dead`, ver `MailQueueConsumer` — BACKLOG.md #7:
    hasta 3 reintentos con backoff antes de llegar acá, con el motivo y el payload original
    completo para reprocesar a mano)
   ```sql
   Host = "postino" AND MessageTemplate LIKE "%agotó los % reintentos — va a dead-letter%"
   ```
   → Dispara alerta si count > 0; cada mensaje trae `MessageId`, `TenantId`,
   `CallerClientId`, `CorrelationId` y `NotificationType` para seguirlo entre el request,
   RabbitMQ, journal y DLQ sin persistir el destinatario ni el cuerpo.

3. **Latencia Alta**
   ```sql
   Host = "postino" AND @Duration > 5000
   ```
   → Dispara alerta si promedio > 5 segundos

### Health Check Endpoint

```bash
curl http://localhost:5000/health
# Response: HTTP 200 si Redis + RabbitMQ están OK
```

Con observabilidad:
```bash
curl http://localhost:5000/health/live
# Response: HTTP 200 (solo verifica startup)

curl http://localhost:5000/health/ready  
# Response: HTTP 200 (verifica readiness después de startup)
```

## Configuración (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

En producción, la **connection string de Seq** se inyecta automáticamente vía `AddServiceDefaults()` desde variables de entorno.

## Troubleshooting

| Problema | Causa | Solución |
|----------|-------|----------|
| "No logs in Seq" | Seq no está accesible | Verificar `dotnet run` en AppHost, Puerto 5341 |
| "Logs sin TraceId" | Logging no inicializado | Verificar `AddServiceDefaults()` en Program.cs |
| "Trazas incompletas" | Spans no se cierran | Verificar `using` statements en spans |
| "Alto volumen de logs" | LogLevel demasiado bajo | Aumentar a "Warning" en Microsoft.AspNetCore |

## Referencias

- [Tero.ServiceDefaults](../../shared/Tero.ServiceDefaults/Extensions.cs)
- [Aspire Seq Integration](https://learn.microsoft.com/en-us/dotnet/aspire/reference/aspire-dashboard)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/getting-started/)
- [Seq Documentation](https://docs.datalust.co/article/getting-started)
