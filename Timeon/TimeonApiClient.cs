#nullable enable

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TabTeams.Timeon;

// Cliente HTTP sellado que encapsula llamadas a la API de Timeon.
// Responsable de:
// - Resolver tokens (SSO o intercambio SSO->JWT).
// - Llamadas GET/POST a endpoints de Timeon.
// - Manejo básico de errores y parsing flexible de respuestas JSON.
public sealed class TimeonApiClient : ITimeonApiClient
{
    // Opciones de JsonSerializer: ignorar mayúsculas/minúsculas en nombres de propiedades.
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    // Caché del JWT devuelto por el endpoint de intercambio SSO -> JWT.
    // Se cachea para evitar canjes repetidos; se invalida con base en _cachedJwtExpiryUtc.
    private string? _cachedJwtFromExchange;
    private DateTimeOffset _cachedJwtExpiryUtc = DateTimeOffset.MinValue;

    public TimeonApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // Obtiene los datos del dashboard para un usuario (por email).
    // - Valida email.
    // - Si está activado UseMockData, devuelve mock.
    // - Busca empleado entre los usuarios devueltos por la API.
    // - Recupera el timelog abierto del empleado (si existe).
    // - Devuelve TimeonDashboardData con posibles ErrorMessage en caso de fallo.
    public async Task<TimeonDashboardData> GetDashboardAsync(string email, string? accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new TimeonDashboardData
            {
                ErrorMessage = "No se pudo obtener el email del usuario de Teams."
            };
        }

        if (UseMockData())
        {
            return BuildMockDashboard(email);
        }

        try
        {
            // Obtener todos los usuarios y buscar coincidencia por email (case-insensitive).
            var users = await GetUsersAsync(accessToken, cancellationToken);
            var employee = users.FirstOrDefault(user =>
                string.Equals(user.Email?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

            if (employee is null)
            {
                return new TimeonDashboardData
                {
                    ErrorMessage = $"No se encontró un empleado en Timeon para el email {email}."
                };
            }



            // Si existe, obtener el time log abierto (si lo hay)
            var openTimeLog = await GetOpenTimeLogAsync(employee.Id, accessToken, cancellationToken);
            return new TimeonDashboardData
            {
                Employee = employee,
                OpenTimeLog = openTimeLog
            };
        }
        catch (UnauthorizedAccessException unauthorizedException)
        {
            // Excepciones de autorización se mapean como ErrorMessage para que la UI lo muestre.
            return new TimeonDashboardData
            {
                ErrorMessage = unauthorizedException.Message
            };
        }
        catch (Exception exception)
        {
            // Cualquier otro error se captura y se presenta en ErrorMessage.
            return new TimeonDashboardData
            {
                ErrorMessage = $"Error consultando Timeon API: {exception.Message}"
            };
        }
    }

    // Operaciones de escritura en la API que delegan a ExecutePostAsync:
    // - StartWorkAsync, StartBreakAsync, EndBreakAsync, EndWorkAsync
    // Cada una construye el payload adecuado y un mensaje de éxito legible.
    public Task<TimeonActionResult> StartWorkAsync(int employeeId, string? accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePostAsync(
            "/api/TimeLog/starttimelog",
            new TimeLogRequest
            {
                EmployeeId = employeeId
            },
            accessToken,
            "Entrada registrada.",
            cancellationToken);
    }

    public Task<TimeonActionResult> StartBreakAsync(int employeeId, int? timeLogId, string? accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePostAsync(
            "/api/TimeLog/startbreak",
            new IdRequest
            {
                // En Timeon API StartBreak espera el id del empleado.
                Id = employeeId
            },
            accessToken,
            "Inicio de pausa registrado.",
            cancellationToken);
    }

    public Task<TimeonActionResult> EndBreakAsync(int employeeId, int? timeLogId, string? accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePostAsync(
            "/api/TimeLog/endbreak",
            new IdRequest
            {
                // En Timeon API EndBreak espera el id del empleado.
                Id = employeeId
            },
            accessToken,
            "Fin de pausa registrado.",
            cancellationToken);
    }

    public Task<TimeonActionResult> EndWorkAsync(int employeeId, string? accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePostAsync(
            "/api/TimeLog/endtimelog",
            new TimeLogRequest
            {
                EmployeeId = employeeId
            },
            accessToken,
            "Salida registrada.",
            cancellationToken);
    }

    // Método central para realizar POST y manejar respuestas/errores de forma consistente.
    // - Si UseMockData devuelve resultado simulado.
    // - Resuelve token con ResolveApiAccessTokenAsync (puede canjear SSO).
    // - Añade Bearer token, hace la petición y parsea el cuerpo de respuesta.
    // - Devuelve TimeonActionResult con Success y Message.
    private async Task<TimeonActionResult> ExecutePostAsync(string relativeUrl, object payload, string? accessToken, string successMessage, CancellationToken cancellationToken)
    {
        if (UseMockData())
        {
            return new TimeonActionResult
            {
                Success = true,
                Message = $"{successMessage} (modo mock)"
            };
        }

        try
        {
            var apiToken = await ResolveApiAccessTokenAsync(accessToken, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = JsonContent.Create(payload, options: JsonSerializerOptions)
            };
            AddBearerToken(request, apiToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var actionPayload = TryReadActionPayload(responseBody);
                if (actionPayload.Success.HasValue)
                {
                    return new TimeonActionResult
                    {
                        Success = actionPayload.Success.Value,
                        Message = string.IsNullOrWhiteSpace(actionPayload.Message)
                            ? (actionPayload.Success.Value ? successMessage : "La operación no se pudo completar.")
                            : actionPayload.Message
                    };
                }
                return new TimeonActionResult
                {
                    Success = true,
                    Message = successMessage
                };
            }

            // Manejo explícito de estados de autorización
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new TimeonActionResult
                {
                    Success = false,
                    Message = "Timeon API rechazó el token. Revisa si debes habilitar SSO directo o canjear SSO por JWT propio en la API."
                };
            }

            // Para otros códigos, devolver un mensaje compacto con el body (no todo el JSON)
            return new TimeonActionResult
            {
                Success = false,
                Message = $"Error {(int)response.StatusCode} en {relativeUrl}: {GetCompactErrorMessage(responseBody)}"
            };
        }
        catch (Exception exception)
        {
            // Captura genérica: encapsula el error en el mensaje devuelto
            return new TimeonActionResult
            {
                Success = false,
                Message = $"No se pudo ejecutar la operación en Timeon API: {exception.Message}"
            };
        }
    }

    // Recupera listado de usuarios desde /api/User/all
    // - Usa GetAsStringAsync para manejar token/respuesta y excepciones de autorización.
    // - El parsing es tolerante: acepta array directo o { data: [] } u otras estructuras comunes.
    private async Task<List<TimeonUser>> GetUsersAsync(string? accessToken, CancellationToken cancellationToken)
    {
        var responseJson = await GetAsStringAsync("/api/User/all", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.ValueKind is JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<TimeonUser>>(document.RootElement.GetRawText(), JsonSerializerOptions) ?? [];
        }

        if (TryGetPropertyArray(document.RootElement, out var arrayElement))
        {
            // Soporta respuestas con envoltorio: { data: [...], result: [...] }
            return JsonSerializer.Deserialize<List<TimeonUser>>(arrayElement.GetRawText(), JsonSerializerOptions) ?? [];
        }

        return [];
    }

    // Obtiene el time log abierto de un empleado.
    // El método tolera varias formas de respuesta:
    // - null/undefined -> devuelve null
    // - array -> toma el primer objeto
    // - objeto directo con propiedades del timelog
    // - objeto envuelto en { data: { timelog: {...} } } etc.
    private async Task<TimeonTimeLog?> GetOpenTimeLogAsync(int employeeId, string? accessToken, CancellationToken cancellationToken)
    {
        var responseJson = await GetAsStringAsync($"/api/TimeLog/timelogopenbyemployee/{employeeId}", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (root.ValueKind is JsonValueKind.Array)
        {
            // Si viene un array, tomar primer elemento si es objeto
            var firstItem = root.EnumerateArray().FirstOrDefault();
            if (firstItem.ValueKind is JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<TimeonTimeLog>(firstItem.GetRawText(), JsonSerializerOptions);
            }
            return null;
        }

        // Si el root es objeto y contiene "id" asumimos que es el mismo timelog
        if (root.ValueKind is JsonValueKind.Object && root.TryGetProperty("id", out _))
        {
            return JsonSerializer.Deserialize<TimeonTimeLog>(root.GetRawText(), JsonSerializerOptions);
        }

        // Si viene envuelto (ej. { data: { timelog: {...} } }) intentar extraer el objeto
        if (root.ValueKind is JsonValueKind.Object && TryGetPropertyObject(root, out var objectElement))
        {
            return JsonSerializer.Deserialize<TimeonTimeLog>(objectElement.GetRawText(), JsonSerializerOptions);
        }

        return null;
    }

    // Método general para hacer GET y devolver el body como string.
    // - Resuelve token vía ResolveApiAccessTokenAsync.
    // - Añade Authorization header si procede.
    // - Si la respuesta es 401/403 lanza UnauthorizedAccessException para que el llamador lo maneje.
    // - En otros errores se invoca response.EnsureSuccessStatusCode() para lanzar excepción con detalle.
    private async Task<string> GetAsStringAsync(string relativeUrl, string? accessToken, CancellationToken cancellationToken)
    {
        var apiToken = await ResolveApiAccessTokenAsync(accessToken, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        AddBearerToken(request, apiToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Excepción específica para que el llamador pueda reaccionar (ej. mostrar mensaje de autorización).
            throw new UnauthorizedAccessException("Timeon API no acepta el token recibido. Necesitas SSO directo en API o endpoint de canje SSO -> JWT.");
        }

        response.EnsureSuccessStatusCode();
        return responseBody;
    }

    // Añade header Authorization: Bearer <token> si token no es nulo/vacío.
    private static void AddBearerToken(HttpRequestMessage request, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    // Resuelve el token que se enviará a la API:
    // - Si no hay ssoToken devuelve null (petición sin token).
    // - Si no está configurado punto de intercambio (TimeonApi:SsoExchangeEndpoint), usa el SSO tal cual.
    // - Si hay endpoint, intenta usar caché y, si procede, canjear el SSO por un JWT propio mediante POST.
    // - Si el intercambio falla lanza UnauthorizedAccessException con mensaje compacto.
    private async Task<string?> ResolveApiAccessTokenAsync(string? ssoToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ssoToken))
        {
            return null;
        }

        var exchangeEndpoint = _configuration["TimeonApi:SsoExchangeEndpoint"];
        if (string.IsNullOrWhiteSpace(exchangeEndpoint))
        {
            // No hay endpoint de intercambio configurado: usar token SSO tal cual.
            return ssoToken;
        }

        // Uso de caché del JWT canjeado. Se considera válido si su expiración es > 1 minuto en el futuro.
        if (!string.IsNullOrWhiteSpace(_cachedJwtFromExchange) && _cachedJwtExpiryUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _cachedJwtFromExchange;
        }

        // Construcción de la petición de intercambio.
        using var request = new HttpRequestMessage(HttpMethod.Post, exchangeEndpoint)
        {
            Content = JsonContent.Create(new
            {
                entraToken = ssoToken,
                ssoToken
            }, options: JsonSerializerOptions)
        };
        // Además se envía el SSO en header Authorization por compatibilidad con algunos endpoints.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ssoToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Lanza excepción de autorización con mensaje compacto (GetCompactErrorMessage).
            throw new UnauthorizedAccessException($"Error al canjear token SSO por JWT Timeon: {(int)response.StatusCode}. {GetCompactErrorMessage(responseBody)}");
        }
        var exchangePayload = TryReadExchangePayload(responseBody);
        var exchangedToken = exchangePayload.Token;
        if (string.IsNullOrWhiteSpace(exchangedToken))
        {
            var message = string.IsNullOrWhiteSpace(exchangePayload.Message)
                ? "El endpoint de canje respondió sin token JWT válido."
                : exchangePayload.Message;
            throw new UnauthorizedAccessException(message);
        }

        // Actualizar caché con el nuevo token y su expiración (si puede leerse del JWT)
        _cachedJwtFromExchange = exchangedToken;
        _cachedJwtExpiryUtc = TryReadJwtExpirationUtc(exchangedToken) ?? DateTimeOffset.UtcNow.AddMinutes(10);
        return _cachedJwtFromExchange;
    }

    // Comprueba configuración para modo mock
    private bool UseMockData()
    {
        return _configuration.GetValue("TimeonApi:UseMockData", false);
    }

    // Intenta leer token y/o mensaje desde el payload de respuesta del endpoint de intercambio.
    // - Soporta payload string (token directo) o objeto con propiedades comunes: token, accessToken, jwt, jwtToken, message.
    // - En fallos de parseo devuelve (null, null).
    private static (string? Token, string? Message) TryReadExchangePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                // Respuesta simple: "eyJ...."
                return (root.GetString(), null);
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                string? token = null;
                string? message = null;

                foreach (var property in root.EnumerateObject())
                {
                    var propertyName = property.Name;
                    if (token is null &&
                        (propertyName.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                         propertyName.Equals("accessToken", StringComparison.OrdinalIgnoreCase) ||
                         propertyName.Equals("jwt", StringComparison.OrdinalIgnoreCase) ||
                         propertyName.Equals("jwtToken", StringComparison.OrdinalIgnoreCase)) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        token = property.Value.GetString();
                        continue;
                    }

                    if (message is null &&
                        propertyName.Equals("message", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        message = property.Value.GetString();
                    }
                }

                return (token, message);
            }
        }
        catch
        {
            // Parseo fallido: devolvemos nulos para que el llamador trate el error.
            return (null, null);
        }
        return (null, null);
    }

    // Intenta leer resultado funcional estándar de Timeon ({ flag, message }) en respuestas HTTP 200.
    // Si no encuentra propiedades conocidas, devuelve (null, null) para usar fallback.
    private static (bool? Success, string? Message) TryReadActionPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            bool? success = null;
            string? message = null;

            foreach (var property in root.EnumerateObject())
            {
                var propertyName = property.Name;
                if (success is null &&
                    (propertyName.Equals("flag", StringComparison.OrdinalIgnoreCase) ||
                     propertyName.Equals("success", StringComparison.OrdinalIgnoreCase) ||
                     propertyName.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
                    (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False))
                {
                    success = property.Value.GetBoolean();
                    continue;
                }

                if (message is null &&
                    (propertyName.Equals("message", StringComparison.OrdinalIgnoreCase) ||
                     propertyName.Equals("detail", StringComparison.OrdinalIgnoreCase) ||
                     propertyName.Equals("error", StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    message = property.Value.GetString();
                }
            }

            return (success, message);
        }
        catch
        {
            return (null, null);
        }
    }

    // Intenta leer el claim "exp" del JWT para obtener la expiración en UTC.
    // Si no puede parsear el JWT o no existe el claim, devuelve null.
    private static DateTimeOffset? TryReadJwtExpirationUtc(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1]
                .PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')
                .Replace('-', '+')
                .Replace('_', '/');

            var payloadBytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(payloadBytes);
            if (document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var expUnix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expUnix);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    // Construye un dashboard de ejemplo para modo mock.
    // - Genera nombre a partir del email y un time log abierto de ejemplo.
    private static TimeonDashboardData BuildMockDashboard(string email)
    {
        var firstName = email.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var displayName = string.IsNullOrWhiteSpace(firstName) ? "Empleado" : char.ToUpperInvariant(firstName[0]) + firstName[1..];

        return new TimeonDashboardData
        {
            IsFromMockData = true,
            Employee = new TimeonUser
            {
                Id = 1001,
                UserName = displayName,
                LastName = "Timeon",
                Email = email,
                Role = "Employee",
                ApplicationCompanyId = 1
            },
            OpenTimeLog = new TimeonTimeLog
            {
                Id = 8001,
                Start = DateTimeOffset.Now.AddHours(-2).AddMinutes(-15),
                Status = "Working"
            }
        };
    }

    // Helpers para parsing flexible de JSON con envolturas comunes.
    // TryGetPropertyArray: busca propiedades que contengan arrays útiles: data, result, items, users.
    private static bool TryGetPropertyArray(JsonElement element, out JsonElement arrayElement)
    {
        foreach (var propertyName in new[] { "data", "result", "items", "users" })
        {
            if (element.TryGetProperty(propertyName, out arrayElement) && arrayElement.ValueKind is JsonValueKind.Array)
            {
                return true;
            }
        }

        arrayElement = default;
        return false;
    }

    // TryGetPropertyObject: busca propiedades que contengan objetos útiles: data, result, item, timelog.
    private static bool TryGetPropertyObject(JsonElement element, out JsonElement objectElement)
    {
        foreach (var propertyName in new[] { "data", "result", "item", "timelog" })
        {
            if (element.TryGetProperty(propertyName, out objectElement) && objectElement.ValueKind is JsonValueKind.Object)
            {
                return true;
            }
        }

        objectElement = default;
        return false;
    }

    // Devuelve una versión compacta del body de error para incluir en mensajes (una sola línea y truncada a 160 caracteres).
    private static string GetCompactErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "sin detalle";
        }

        var singleLine = responseBody.Replace("\r", " ").Replace("\n", " ").Trim();
        if (singleLine.Length <= 160)
        {
            return singleLine;
        }

        return $"{singleLine[..160]}...";
    }
}