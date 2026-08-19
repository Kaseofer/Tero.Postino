# Postino scaffold — Clean Architecture template

Este directorio contiene un scaffold para crear el microservicio "Tero.Postino" (servicio de envío de mails) como un repositorio Git independiente siguiendo una estructura Clean Architecture similar a Tero.Auth.

Contenido propuesto

- src/Tero.Postino.Api — API / Worker (host)
- src/Tero.Postino.Application — casos de uso, puertos (interfaces)
- src/Tero.Postino.Domain — entidades y modelos de dominio
- src/Tero.Postino.Infrastructure — implementaciones (SMTP, RabbitMQ consumer, persistence)
- Tero.Postino.sln — solución (opcional, creada localmente)

Objetivo

- Generar un repo independiente en GitHub y añadirlo como submódulo dentro del repo principal (apphost). El scaffold ayuda a iniciar el repo con la estructura, csproj y ejemplos mínimos.

Cómo usar (resumen)

1. Desde la carpeta scaffold/Tero.Postino ejecutá el script create-remote.ps1 para crear el repositorio remoto (requiere GitHub CLI `gh`, o seguir pasos manuales):

   .\create-remote.ps1 -RepoName "Tero.Postino" -Description "Mail delivery microservice (Postino)"

   El script crea el repo remoto, inicializa un repo local, hace commit y push.

2. Alternativa manual:

   - Crear repositorio en GitHub (UI) con el nombre Tero.Postino.
   - Desde scaffold/Tero.Postino:
	 git init
	 git add .
	 git commit -m "Initial scaffold"
	 git remote add origin git@github.com:YOUR_ORG/Tero.Postino.git
	 git branch -M main
	 git push -u origin main

3. Añadir submódulo en el repo principal (apphost):

   cd /path/to/apphost
   git submodule add git@github.com:YOUR_ORG/Tero.Postino.git services/postino
   git commit -m "Add postino submodule"

Notas

- El scaffold es mínimo: adaptar Program.cs y las dependencias según políticas de infra/CI.
- Manejar secretos (SMTP, RabbitMQ) por variables de entorno o KeyVault.
- Incluye ejemplo de PowerShell para crear el repo remoto; tené instalado y autenticado `gh` si usás la opción automática.
