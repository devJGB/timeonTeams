// Program.cs — configuración y arranque de la aplicación Blazor Server con componentes interactivos y cliente HTTP para Timeon.

using TabTeams;
using TabTeams.Components;
using TabTeams.Interop.TeamsSDK;
using TabTeams.Timeon;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Registrar servicios para Razor Components (Blazor) y componentes interactivos en servidor.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Leer configuración fuertemente tipada (ConfigOptions) desde appsettings (opcional).
var config = builder.Configuration.Get<ConfigOptions>();

// Registrar servicios relacionados con Teams (auth/config) usando la configuración leída.
builder.Services.AddTeamsFx(config.TeamsFx.Authentication);

// Servicio scoped que encapsula interop con el SDK de Teams en el cliente.
builder.Services.AddScoped<MicrosoftTeams>();

// Registrar HttpClient para ITimeonApiClient con configuración de base address y timeout.
// Se inyecta HttpClientFactory y se configura por instancia.
builder.Services.AddHttpClient<ITimeonApiClient, TimeonApiClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    // Leer base URL desde configuración; usar URL por defecto si no está presente.
    var baseUrl = configuration["TimeonApi:BaseUrl"] ?? "https://timeonapi-dugye5abd6fbc5dh.spaincentral-01.azurewebsites.net";
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Registrar controladores MVC/Web API por si hay endpoints auxiliares.
builder.Services.AddControllers();
// Cliente HTTP genérico con mayor timeout (nombre "WebClient").
builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));

// Acceso al contexto HTTP (HttpContext) desde DI.
builder.Services.AddHttpContextAccessor();

// Configurar antiforgery y permitir que la cabecera X-Frame-Options sea suprimida
// (necesario cuando se incrusta la app en iframes o en Teams).
builder.Services.AddAntiforgery(o => o.SuppressXFrameOptionsHeader = true);

// Registrar la configuración de la librería Fluent UI (u otra configuración global).
builder.Services.AddSingleton<LibraryConfiguration>();

var app = builder.Build();

// Pipeline: manejo de errores según entorno.
if (app.Environment.IsDevelopment())
{
    // Mostrar página de excepción detallada en desarrollo.
    app.UseDeveloperExceptionPage();
}
else
{
    // En producción, usar handler genérico y HSTS.
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Servir archivos estáticos (wwwroot).
app.UseStaticFiles();

// Routing y middlewares necesarios: antiforgery, autenticación y autorización.
app.UseRouting();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Mapear la app Blazor (Razor Components) al root y habilitar modo interactivo en servidor.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Iniciar la aplicación web.
app.Run();