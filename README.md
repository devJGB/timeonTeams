# TabTeams (Timeon for Microsoft Teams)
Aplicación de pestaña (tab) para Microsoft Teams que permite fichar en Timeon (entrada, inicio pausa, fin pausa y salida) usando SSO de Teams.
Microsoft Teams tab application for Timeon clock-in actions (start shift, start break, end break, end shift) using Teams SSO.
## Español
### 1) Resumen
`TabTeams` es una aplicación Blazor Server integrada en Microsoft Teams.  
Autentica al usuario con Teams SSO, obtiene un JWT de Timeon mediante canje de token y muestra un panel de fichaje con acciones en tiempo real.
### 2) Funcionalidad principal
- Inicio de sesión con identidad de Teams (SSO).
- Carga de datos del empleado y fichaje abierto.
- Acciones de fichaje:
  - Entrada
  - Inicio pausa
  - Fin pausa
  - Salida
- Soporte visual para temas de Teams:
  - Claro
  - Oscuro
  - Alto contraste
- Modo de datos simulados (mock) configurable para pruebas.
### 3) Flujo de autenticación (alto nivel)
1. La tab obtiene el token SSO de Teams.
2. La app llama al endpoint de canje configurado en `TimeonApi:SsoExchangeEndpoint`.
3. El backend de Timeon devuelve JWT propio de Timeon.
4. Con ese JWT, la tab consume endpoints funcionales de Timeon para dashboard y acciones.
### 4) Estructura de archivos clave
- `Program.cs`  
  Arranque de la app, DI, registro de TeamsFx y `HttpClient` para Timeon.
- `Config.cs`  
  Clases de configuración tipada para TeamsFx.
- `appsettings.json` / `appsettings.Development.json`  
  Configuración de TeamsFx y bloque `TimeonApi`.
- `Components/App.razor`, `Components/Routes.razor`  
  Shell y enrutamiento principal.
- `Components/Pages/Tab.razor`  
  Entrada principal de la pestaña Teams.
- `Components/Pages/TimeonDashboard.razor`  
  Lógica UI del dashboard y acciones de fichaje.
- `Components/Pages/TimeonDashboard.razor.css`  
  Estilos del dashboard y adaptación de temas.
- `Timeon/ITimeonApiClient.cs`  
  Contrato del cliente de API Timeon.
- `Timeon/TimeonApiClient.cs`  
  Integración HTTP con Timeon (dashboard, acciones, canje SSO->JWT).
- `Timeon/TimeonModels.cs`  
  Modelos de dominio para dashboard y respuestas.
- `Interop/TeamsSDK/*` y `wwwroot/js/TeamsJsBlazorInterop.js`  
  Interop con SDK de Teams (contexto, tema, integración cliente).
- `M365Agent/*`  
  Archivos de Microsoft 365 Agents Toolkit (manifiestos, env, aprovisionamiento/publicación).
### 5) Configuración relevante
Bloque recomendado en configuración:
- `TimeonApi:BaseUrl`
- `TimeonApi:UseMockData`
- `TimeonApi:SsoExchangeEndpoint`
Importante:
- No guardar secretos reales en documentación pública.
- Gestionar secretos/credenciales con mecanismos seguros (entorno local seguro, variables de entorno, secretos del entorno de ejecución, etc.).
### 6) Ejecución local
1. Restaurar paquetes:
   - `dotnet restore`
2. Compilar:
   - `dotnet build`
3. Ejecutar:
   - `dotnet run --project TabTeams.csproj`
### 7) Troubleshooting rápido
- Si cambios visuales no aparecen en Teams, recargar la pestaña / reinstalar paquete local (caché de Teams).
- Si `TabTeams.exe`/`TabTeams.dll` está bloqueado, cerrar ejecución activa antes de compilar/publicar.
- Si falla autenticación, revisar `BaseUrl`, `SsoExchangeEndpoint` y configuración de app registration/manifiesto.

---
## English
### 1) Overview
`TabTeams` is a Blazor Server app embedded as a Microsoft Teams tab.  
It authenticates users with Teams SSO, exchanges the token for a Timeon JWT, and provides a real-time clock-in dashboard.
### 2) Core features
- Teams SSO-based authentication.
- Employee and open timelog retrieval.
- Clock actions:
  - Start shift
  - Start break
  - End break
  - End shift
- Teams theme support:
  - Light
  - Dark
  - High contrast
- Optional mock mode for testing.
### 3) Authentication flow (high level)
1. The tab gets the Teams SSO token.
2. The app calls the exchange endpoint configured in `TimeonApi:SsoExchangeEndpoint`.
3. Timeon backend returns a Timeon JWT.
4. The tab uses that JWT for dashboard and action endpoints.
### 4) Key file structure
- `Program.cs`  
  App bootstrap, DI, TeamsFx registration, and Timeon `HttpClient`.
- `Config.cs`  
  Typed configuration classes for TeamsFx.
- `appsettings.json` / `appsettings.Development.json`  
  TeamsFx and `TimeonApi` settings.
- `Components/App.razor`, `Components/Routes.razor`  
  App shell and routing.
- `Components/Pages/Tab.razor`  
  Main Teams tab entry page.
- `Components/Pages/TimeonDashboard.razor`  
  Dashboard UI logic and clock actions.
- `Components/Pages/TimeonDashboard.razor.css`  
  Dashboard styles and Teams theme adaptation.
- `Timeon/ITimeonApiClient.cs`  
  API client contract.
- `Timeon/TimeonApiClient.cs`  
  Timeon HTTP integration (dashboard, actions, SSO->JWT exchange).
- `Timeon/TimeonModels.cs`  
  Domain models for dashboard and responses.
- `Interop/TeamsSDK/*` and `wwwroot/js/TeamsJsBlazorInterop.js`  
  Teams SDK interop (context, theme, client integration).
- `M365Agent/*`  
  Microsoft 365 Agents Toolkit assets (manifests, env files, provisioning/publishing).
### 5) Relevant configuration
Recommended keys:
- `TimeonApi:BaseUrl`
- `TimeonApi:UseMockData`
- `TimeonApi:SsoExchangeEndpoint`
Important:
- Do not store real secrets in public documentation.
- Use secure secret management for local/runtime environments.
### 6) Local run
1. Restore packages:
   - `dotnet restore`
2. Build:
   - `dotnet build`
3. Run:
   - `dotnet run --project TabTeams.csproj`
### 7) Quick troubleshooting
- If UI changes are not visible in Teams, reload tab / reinstall local package (Teams caching).
- If files are locked during build/publish, stop running app processes first.
- If authentication fails, verify `BaseUrl`, `SsoExchangeEndpoint`, and app registration/manifest alignment.

