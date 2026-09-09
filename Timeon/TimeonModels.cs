namespace TabTeams.Timeon;

public sealed class TimeonDashboardData
{
    public TimeonUser? Employee { get; init; }
    public TimeonTimeLog? OpenTimeLog { get; init; }

    // Colección de fichajes del día para mostrar el resumen debajo de la cabecera.
    // Incluye el fichaje principial abierto si existe y también los ya finalizados.
    public IReadOnlyList<TimeonTimeLog> DayTimeLogs { get; init; } 
    public string? ErrorMessage { get; init; }
    public DateTimeOffset LoadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsFromMockData { get; init; }
}

public sealed class TimeonActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class TimeonUser
{
    public int Id { get; set; }
    public string? UserName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public int ApplicationCompanyId { get; set; }
}

public sealed class TimeonTimeLog
{
    public int Id { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? StartBreak { get; set; }
    public DateTimeOffset? EndBreak { get; set; }
    public DateTimeOffset? End { get; set; }
    public string? Status { get; set; }
}

internal sealed class TimeLogRequest
{
    public int EmployeeId { get; set; }
    public string? Geolocation { get; set; }
    public string? Address { get; set; }
}

internal sealed class IdRequest
{
    public int Id { get; set; }
}
