using System.Diagnostics;

namespace Clinic.API;

// A server launched by Desktop must also stop when Rider forcibly stops its parent.
internal sealed class ParentProcessLifetime(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("CLINIC_PARENT_PROCESS_ID"), out var id) || id <= 0)
            return;
        try
        {
            using var parent = Process.GetProcessById(id);
            await parent.WaitForExitAsync(stoppingToken);
            lifetime.StopApplication();
        }
        catch (ArgumentException) { lifetime.StopApplication(); }
        catch (InvalidOperationException) { lifetime.StopApplication(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
