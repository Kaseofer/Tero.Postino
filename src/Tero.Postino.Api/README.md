# Tero.Postino

Microservicio de entrega de correos del ecosistema Tero. 

**Características**:
- ✅ API REST con autenticación de servicio interno (headers `X-Tero-Service-Id` + `X-Tero-Service-Token`)
- ✅ Procesamiento asincrónico con RabbitMQ
- ✅ Endpoints: verificación email, reset de contraseña, notificaciones de citas
- ✅ Logs centralizados en Seq (OpenTelemetry OTLP)
- ✅ Observabilidad: distributed tracing, structured logging, health checks
- ✅ Outcome-based application layer (patrón uniforme con Auth.Api)

## Documentación

- **[SECURITY.md](./SECURITY.md)** — Autenticación de servicios internos, tokens, header requeridos
- **[OBSERVABILITY.md](./OBSERVABILITY.md)** — Logs en Seq, OpenTelemetry, distributed tracing, búsquedas útiles

## Credenciales (obligatorias para el build)

Este repo consume los paquetes compartidos (`Tero.Contracts`, `Tero.Messaging`,
`Tero.Persistence`, `Tero.ServiceDefaults`) desde GitHub Packages, que exige autenticación
incluso para lectura. Antes de restaurar/compilar, configurar dos variables de entorno con
un PAT clásico con scope `read:packages` (ver `nuget.config`):

```
GITHUB_PACKAGES_USER=<tu usuario de GitHub>
GITHUB_PACKAGES_TOKEN=<tu PAT con read:packages>
```

Sin estas variables el `dotnet restore` falla con 401.

## Secretos de desarrollo

Ningún secreto va en `appsettings*.json` versionado. En local se configuran por
`dotnet user-secrets` (el `UserSecretsId` ya está declarado en
`Tero.Postino.csproj`); en otros entornos, por variable de entorno o el
secret store que corresponda.

```
cd src/services/Tero.Postino
dotnet user-secrets set "ConnectionStrings:tero" "Host=localhost;Port=5432;Database=tero;Username=postgres;Password=postgres"
```

`ConnectionStrings:tero` sólo hace falta si corrés el servicio standalone (`dotnet run`),
sin el AppHost del repo padre — el AppHost la inyecta solo.

## Build y test

```
dotnet build
dotnet test
```

0 errores, 0 advertencias (`TreatWarningsAsErrors`).

## Desarrollo Local (con Aspire AppHost)

```bash
# En repo root
cd src/Tero.AppHost
dotnet run

# Aspire Dashboard: http://localhost:15001
# - Postino: http://localhost:5000
# - Seq (logs): http://localhost:5341
# - RabbitMQ: http://localhost:15672
```

## API Endpoint Ejemplo

```bash
# Enviar email de verificación (solo desde servicios autorizados)
curl -X POST http://localhost:5000/api/email/verify-email \
  -H "Content-Type: application/json" \
  -H "X-Tero-Service-Id: auth-api" \
  -H "X-Tero-Service-Token: auth-api-token-change-in-production" \
  -d '{
    "recipientEmail": "user@example.com",
    "userName": "Juan",
    "verificationToken": "ABC123",
    "verificationUrl": "https://app.com/verify?token=ABC123",
    "priority": "normal"
  }'

# Response: 202 Accepted
```

## Integración con Seq

Los logs de Postino se **centralizan automáticamente en Seq** via OpenTelemetry OTLP:

1. `builder.AddServiceDefaults()` en `Program.cs` registra OpenTelemetry
2. AppHost inyecta `ConnectionStrings__seq`
3. ServiceDefaults configura OTLP exporter hacia `{seq}/ingest/otlp/`
4. Búsqueda en Seq: `Host = "postino"`

Ver [OBSERVABILITY.md](./OBSERVABILITY.md) para más detalles, búsquedas útiles y alertas.
