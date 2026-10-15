using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Clinic.Desktop;

// Only a configured source checkout owns a local server. Published clients use their API address.
internal sealed class LocalApiProcess(string root, Uri address) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Process? _process;
    internal int? OwnedProcessId => _process is { HasExited: false } process ? process.Id : null;

    internal static LocalApiProcess? ForCheckout(string? configuredAddress, string? baseDirectory = null)
    {
        // An explicit URL always belongs to the operator, including a loopback URL.
        if (!string.IsNullOrWhiteSpace(configuredAddress)) return null;
        for (var directory = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = directory.FullName;
            if (File.Exists(Path.Combine(path, "Clinic.slnx")) &&
                File.Exists(Path.Combine(path, "Clinic.Desktop", "Clinic.Desktop.csproj")) &&
                File.Exists(Path.Combine(path, "Clinic.API", "api.local.json")))
                return new(path, new Uri("http://127.0.0.1:5278"));
        }
        return null;
    }

    public async Task EnsureReadyAsync()
    {
        await _gate.WaitAsync(_shutdown.Token);
        try
        {
            if (await IsReadyAsync(_shutdown.Token)) return;
            if (_process is null || _process.HasExited) Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    if (await IsReadyAsync(timeout.Token)) return;
                    if (_process!.HasExited)
                        throw new InvalidOperationException("Не удалось запустить сервис поликлиники. Проверьте настройки сервера и повторите вход.");
                    await Task.Delay(250, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
            {
                throw new InvalidOperationException("Сервис поликлиники не успел запуститься. Повторите вход через несколько секунд.");
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(address, "health"), cancellationToken);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                throw new InvalidOperationException("Сервис поликлиники работает, но база данных недоступна. Проверьте, что PostgreSQL запущен.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Сервис поликлиники недоступен. Проверьте настройки сервера.");
            using var health = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!health.RootElement.TryGetProperty("status", out var status) || status.GetString() != "ok")
                throw new InvalidOperationException("По адресу поликлиники отвечает другой сервис. Проверьте настройки сервера.");
            return true;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException
               { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused }) { return false; }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("По адресу поликлиники отвечает другой сервис. Проверьте настройки сервера.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Сервис поликлиники не ответил вовремя. Повторите вход.");
        }
    }

    private void Start()
    {
        var configuration = typeof(LocalApiProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var apiDirectory = Path.Combine(root, "Clinic.API");
        var assembly = Path.Combine(apiDirectory, "bin", configuration, "net10.0", "Clinic.API.dll");
        if (!File.Exists(assembly))
            throw new InvalidOperationException("Сервис поликлиники не собран. Выполните сборку проекта и повторите вход.");

        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(File.Exists(host) ? host : "dotnet")
        {
            WorkingDirectory = apiDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(address.ToString().TrimEnd('/'));
        start.Environment["CLINIC_PARENT_PROCESS_ID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["TZ"] = "Europe/Moscow";
        start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Warning";
        _process?.Dispose();
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Trace.WriteLine("[Clinic.API] " + e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Trace.WriteLine("[Clinic.API] " + e.Data); };
        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Trace.WriteLine(ex);
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("Не удалось запустить сервис поликлиники. Проверьте установку .NET 10.");
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        if (_process is not null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
        }
        _http.Dispose();
    }
}
