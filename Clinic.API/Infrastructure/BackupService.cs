using System.ComponentModel;
using System.Diagnostics;
using Npgsql;

namespace Clinic.API;

// Резервное копирование через pg_dump / pg_restore.
// Пути и папка задаются в appsettings.json, секция "Backup" (см. ENDPOINTS.md).
public sealed class BackupService(IConfiguration config, ILogger<BackupService> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public string Folder => Path.GetFullPath(config["Backup:Folder"] ?? "Backups");
    private string PgDump => config["Backup:PgDumpPath"] ?? "pg_dump";
    private string PgRestore => config["Backup:PgRestorePath"] ?? "pg_restore";

    public IReadOnlyList<BackupFileDto> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return new DirectoryInfo(Folder).GetFiles("*.backup")
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupFileDto(f.Name, f.Length, f.CreationTime))
            .ToList();
    }

    // Возвращает полный путь к файлу копии или null, если имя некорректное или файла нет.
    // Path.GetFileName не даёт выйти из папки через "../".
    public string? ResolveFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) return null;
        var path = Path.Combine(Folder, fileName);
        return File.Exists(path) ? path : null;
    }

    public async Task<(bool Ok, string Message, string? FileName)> CreateAsync(string connectionString)
    {
        Directory.CreateDirectory(Folder);
        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        var fileName = $"{cs.Database}_{DateTime.Now:yyyyMMdd_HHmmss}.backup";
        var path = Path.Combine(Folder, fileName);

        var (ok, message) = await RunAsync(PgDump, cs, ["-F", "c", "-f", path, "-d", cs.Database ?? ""]);
        if (!ok && File.Exists(path)) File.Delete(path);   // не оставляем битый файл
        return (ok, ok ? "Резервная копия создана" : message, ok ? fileName : null);
    }

    public async Task<(bool Ok, string Message)> RestoreAsync(string connectionString, string path)
    {
        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        NpgsqlConnection.ClearAllPools();   // закрываем свои соединения с базой
        var (ok, message) = await RunAsync(PgRestore, cs,
            ["--clean", "--if-exists", "--no-owner", "--single-transaction", "-d", cs.Database ?? "", path]);
        return (ok, ok ? "База восстановлена из копии" : message);
    }

    private async Task<(bool Ok, string Message)> RunAsync(string exe, NpgsqlConnectionStringBuilder cs, string[] args)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-h"); info.ArgumentList.Add(cs.Host ?? "localhost");
        info.ArgumentList.Add("-p"); info.ArgumentList.Add(cs.Port.ToString());
        info.ArgumentList.Add("-U"); info.ArgumentList.Add(cs.Username ?? "postgres");
        info.ArgumentList.Add("--no-password");
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["PGPASSWORD"] = cs.Password ?? "";   // пароль не попадает в командную строку

        using var timeout = new CancellationTokenSource(Timeout);
        Process? process = null;
        try
        {
            process = Process.Start(info)!;
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stderr = await stderrTask;

            if (process.ExitCode == 0) return (true, "");
            logger.LogError("{Exe} завершился с кодом {Code}: {Error}", exe, process.ExitCode, stderr);
            return (false, $"{Path.GetFileName(exe)} завершился с ошибкой: {stderr.Trim()}");
        }
        catch (Win32Exception)
        {
            return (false, $"Не найдена программа {exe}. Укажите полный путь в appsettings.json (Backup:PgDumpPath / Backup:PgRestorePath)");
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(entireProcessTree: true); } catch { /* процесс уже завершился */ }
            return (false, "Операция не уложилась в 10 минут и была прервана");
        }
        finally
        {
            process?.Dispose();
        }
    }
}
