# 🔐 Seguridad en Tero.Postino - Autorización de Microservicios

## Descripción General

Tero.Postino solo acepta solicitudes de microservicios autorizados dentro del ecosistema Tero. Esto se logra mediante autenticación basada en tokens de servicio.

**Solo microservicios registrados pueden enviar correos.**

---

## 📋 Microservicios Autorizados

Los siguientes microservicios están autorizados para usar Postino:

| Servicio ID | Descripción | Endpoint |
|------------|-------------|----------|
| **auth-api** | Autenticación y gestión de usuarios | http://auth-api:5000 |
| **appointments-api** | Gestión de citas | http://appointments-api:5000 |
| **whatsapp-gateway** | Gateway de WhatsApp | http://whatsapp-gateway:5000 |

---

## 🔑 Autenticación por Headers

Todos los requests a Postino **DEBEN incluir** dos headers HTTP:

### Header 1: X-Tero-Service-Id
Identificador único del microservicio que realiza la solicitud.

```
X-Tero-Service-Id: auth-api
```

**Formato válido:**
- Solo minúsculas, números y guiones
- Ejemplo: `auth-api`, `appointments-api`, `whatsapp-gateway`

### Header 2: X-Tero-Service-Token
Token de autenticación secreto del microservicio.

```
X-Tero-Service-Token: auth-api-token-change-in-production
```

**Formato válido:**
- Mínimo 32 caracteres
- Máximo 500 caracteres
- Alfanuméricos, guiones, puntos y guiones bajos permitidos

---

## 📨 Ejemplo de Solicitud Autenticada

### Request Correcto ✅

```bash
curl -X POST https://postino:5000/api/email/verify-email \
  -H "Content-Type: application/json" \
  -H "X-Tero-Service-Id: auth-api" \
  -H "X-Tero-Service-Token: auth-api-token-change-in-production" \
  -d '{
    "recipientEmail": "user@example.com",
    "userName": "Juan",
    "verificationToken": "token123",
    "verificationUrl": "https://app.com/verify?token=token123",
    "priority": "normal"
  }'
```

**Respuesta Exitosa:**
```json
{
  "mailJobId": "550e8400-e29b-41d4-a716-446655440000",
  "success": true,
  "message": "Correo de verificación encolado exitosamente"
}
```

### Request Incorrecto ❌

```bash
# FALTA Header X-Tero-Service-Id
curl -X POST https://postino:5000/api/email/verify-email \
  -H "Content-Type: application/json" \
  -H "X-Tero-Service-Token: auth-api-token-change-in-production" \
  -d '{...}'
```

**Respuesta:**
```json
{
  "error": "Unauthorized",
  "message": "Missing or invalid service identity header",
  "header": "X-Tero-Service-Id"
}
```

---

## ⚙️ Configuración

### En appsettings.json (Desarrollo)

```json
{
  "Tero": {
    "Services": {
      "AuthApi": {
        "Token": "auth-api-token-change-in-production"
      },
      "AppointmentsApi": {
        "Token": "appointments-api-token-change-in-production"
      },
      "WhatsAppGateway": {
        "Token": "whatsapp-gateway-token-change-in-production"
      }
    }
  }
}
```

### En Producción (Secrets / Env Vars)

**NUNCA** incluir tokens en appsettings.json en producción.

Usar en su lugar:

```bash
# Variables de entorno
export Tero__Services__AuthApi__Token=<secure-token>
export Tero__Services__AppointmentsApi__Token=<secure-token>
export Tero__Services__WhatsAppGateway__Token=<secure-token>

# O Azure Key Vault
# O AWS Secrets Manager
# O HashiCorp Vault
```

---

## 🛡️ Mejores Prácticas

### ✅ Recomendaciones

1. **Usar Tokens Seguros**
   - Generar tokens con at least 128 bits de entropía
   - Usar herramientas como `openssl rand -hex 64` o `dotnet user-secrets`

2. **Renovación Regular**
   - Cambiar tokens cada 3-6 meses
   - Implementar rotación de tokens

3. **Almacenamiento Seguro**
   - **NUNCA** commitar tokens en Git
   - Usar `.gitignore` para archivos sensibles
   - Usar sistemas de secrets management

4. **Logging y Auditoría**
   - Todos los accesos se registran con timestamp
   - Log incluye: ServiceId, IP origen, timestamp, resultado
   - Revisar logs regularmente

5. **Rotación de Tokens**
   - Implementar un proceso de rotación sin downtime
   - Soportar tokens antiguos y nuevos por un período

6. **Rate Limiting** (Futuro)
   - Implementar límites por servicio
   - Prevenir uso abusivo

---

## 🔍 Validación de Headers

El sistema valida automáticamente:

### ✓ Validación Realizada

1. **Presencia del Header**
   - Verifica que `X-Tero-Service-Id` esté presente
   - Verifica que `X-Tero-Service-Token` esté presente

2. **Formato del Service ID**
   - Solo minúsculas, números y guiones
   - Rechazo: ANY uppercase, espacios, caracteres especiales

3. **Formato del Token**
   - Validar longitud (32-500 caracteres)
   - Validar caracteres (alfanuméricos, guiones, puntos, guiones bajos)

4. **Autenticación**
   - Búsqueda en tabla de servicios autorizados
   - Comparación segura de tokens (contra timing attacks)

5. **Auditoría**
   - Log de intentos fallidos
   - Log de accesos exitosos
   - IP origen registrada

---

## 📝 Códigos HTTP por Autorización

| Código | Razón | Causa |
|--------|-------|-------|
| **202** | Aceptado | Todo está bien, correo encolado |
| **400** | Bad Request | Validación de datos fallida |
| **401** | Unauthorized | Service ID/Token inválido o faltante |
| **403** | Forbidden | Servicio no autorizado* |
| **500** | Server Error | Error interno |

*El sistema retorna 401 para ambos casos (missing/invalid) por seguridad.

---

## 🚀 Integración desde Otro Microservicio

### Ejemplo desde Auth.Api

```csharp
// En Program.cs
builder.Services.AddHttpClient<PostinoClient>(client =>
{
    client.BaseAddress = new Uri("http://postino:5000");

    // Agregar headers automáticamente
    client.DefaultRequestHeaders.Add(
        "X-Tero-Service-Id", "auth-api");
    client.DefaultRequestHeaders.Add(
        "X-Tero-Service-Token", 
        builder.Configuration["Tero:Postino:Token"] ?? throw new InvalidOperationException("Missing Postino token"));
});

// En el use case
var response = await _httpClient.PostAsJsonAsync(
    "/api/email/verify-email",
    new VerifyEmailRequest(...),
    cancellationToken);
```

### Ejemplo desde Appointments.Api

```csharp
// Similar, solo cambiar el Service-Id
client.DefaultRequestHeaders.Add("X-Tero-Service-Id", "appointments-api");
client.DefaultRequestHeaders.Add(
    "X-Tero-Service-Token",
    builder.Configuration["Tero:Postino:Token"]);
```

---

## 🔐 Protección contra Ataques

### Timing Attack Protection
El sistema usa comparación constante de tokens:

```csharp
// PROTEGIDO: Comparison constante
int result = 0;
for (int i = 0; i < bytes1.Length; i++)
{
    result |= bytes1[i] ^ bytes2[i];  // Siempre compara todos los bytes
}
return result == 0;
```

### Prevención de Fuerza Bruta
**Futuro:** Implementar rate limiting por IP/ServiceId.

### Validación de Entrada
- Validar formato de Service ID
- Validar formato de Token
- Validar longitud de Token

---

## 📊 Logging de Seguridad

Todos los eventos de autenticación se registran:

```
[INFO] Request authorized from service 'auth-api'
[WARN] Request missing service identity header (X-Tero-Service-Id)
[WARN] Service 'unknown-service' not found in authorized services list
[WARN] Invalid token provided for service 'auth-api'
[WARN] Unauthorized service attempted access: 'malicious-api'. Remote IP: 192.168.1.100
```

Revisar logs en:
- Desarrollo: Console output
- Producción: Application Insights / Seq

---

## 🔧 Administración de Servicios

### Agregar un Nuevo Servicio

1. **Generar un token seguro:**
   ```bash
   openssl rand -hex 64
   ```

2. **Actualizar appsettings.json (dev):**
   ```json
   {
     "Tero": {
       "Services": {
         "NewService": {
           "Token": "generated-token-here"
         }
       }
     }
   }
   ```

3. **Actualizar Program.cs:**
   ```csharp
   authorizedServices.Add("new-service", 
       builder.Configuration["Tero:Services:NewService:Token"] ?? "...");
   ```

4. **En producción:** Usar secrets management.

### Revocar Acceso de un Servicio

1. **Eliminar de appsettings.json**
2. **Eliminar de Program.cs**
3. **Redeploy**

Inmediatamente, ese servicio recibirá 401 Unauthorized.

---

## 🎯 Checklist de Seguridad

- [ ] Tokens generados con suficiente entropía
- [ ] Tokens almacenados en secrets management (no en Git)
- [ ] `.gitignore` incluye archivos sensibles
- [ ] Logs monitoreados regularmente
- [ ] Acceso a appsettings restringido a admin
- [ ] Rate limiting planeado para futuro
- [ ] Rotación de tokens documentada
- [ ] Todos los microservicios incluyen headers requeridos
- [ ] Pruebas automatizadas de autenticación
- [ ] Plan de recuperación ante brechas de seguridad

---

## 📞 Soporte

¿Nuevo microservicio que necesita acceso?
→ Contactar al equipo de infraestructura
→ Proporcionar: nombre, descripción, equipo responsable

¿Token comprometido?
→ Cambiar inmediatamente en secrets management
→ Auditar logs para accesos no autorizados
→ Registrar incidente de seguridad

---

**Última actualización:** 2024-12-20  
**Versión:** 1.0  
**Status:** Production Ready
