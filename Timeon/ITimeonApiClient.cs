namespace TabTeams.Timeon;

public interface ITimeonApiClient
{
    Task<TimeonDashboardData> GetDashboardAsync(string email, string? accessToken, CancellationToken cancellationToken = default);
    Task<TimeonActionResult> StartWorkAsync(int employeeId, string? accessToken, CancellationToken cancellationToken = default);
    Task<TimeonActionResult> StartBreakAsync(int employeeId, int? timeLogId, string? accessToken, CancellationToken cancellationToken = default);
    Task<TimeonActionResult> EndBreakAsync(int employeeId, int? timeLogId, string? accessToken, CancellationToken cancellationToken = default);
    Task<TimeonActionResult> EndWorkAsync(int employeeId, string? accessToken, CancellationToken cancellationToken = default);
}
