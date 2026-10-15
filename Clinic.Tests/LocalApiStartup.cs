using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Clinic.Desktop;
using Clinic.ViewModels;

internal static class LocalApiStartup
{
    public static async Task RunAsync()
    {
        var root = FindRoot();
        var checks = 0;
        void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        using (var local = LocalApiProcess.ForCheckout(null))
            Assert(local is not null, "Source checkout enables local startup");
        Assert(LocalApiProcess.ForCheckout("https://clinic.example") is null, "Explicit server disables local startup");
        Assert(LocalApiProcess.ForCheckout("http://127.0.0.1:5278") is null, "Explicit loopback server is also external");
        Assert(LocalApiProcess.ForCheckout(null, Path.GetTempPath()) is null, "Published client cannot launch source server");

        var address = FreeAddress();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using (var owned = new LocalApiProcess(root, address))
        {
            await owned.EnsureReadyAsync();
            var health = await http.GetFromJsonAsync<Dictionary<string, string>>(new Uri(address, "health"));
            Assert(health?.GetValueOrDefault("status") == "ok", "Missing API starts and connects to PostgreSQL");
            if (health?.GetValueOrDefault("database") != "clinic_api_integration_test")
                throw new Exception("Login checks require the isolated clinic_api_integration_test database.");
            Assert(owned.OwnedProcessId.HasValue, "Started API belongs to launcher");
            var originalPid = owned.OwnedProcessId;
            using (var peer = new LocalApiProcess(root, address))
            {
                await peer.EnsureReadyAsync();
                Assert(peer.OwnedProcessId is null, "Second client reuses an existing API");
            }
            Assert((await http.GetFromJsonAsync<Dictionary<string, string>>(new Uri(address, "health")))?["status"] == "ok",
                "Closing second client leaves existing API running");

            var vm = new MainViewModel(prepareConnection: owned.EnsureReadyAsync)
                { ApiAddress = address.ToString(), Login = "kirillov", Password = "demo12345" };
            await vm.SignInCommand.ExecuteAsync(null);
            Assert(vm.IsSignedIn && vm.Workspace is not null, "Login opens real patient workspace after readiness check: " + vm.StatusMessage);
            await vm.SignOutCommand.ExecuteAsync(null);

            using (var crashed = Process.GetProcessById(originalPid!.Value))
            {
                crashed.Kill();
                await crashed.WaitForExitAsync();
            }
            await owned.EnsureReadyAsync();
            Assert(owned.OwnedProcessId.HasValue && owned.OwnedProcessId != originalPid,
                "Next login restarts a failed local API");
        }
        await WaitUntilOfflineAsync(http, address);
        Assert(true, "Closing owner stops its API");

        var childAddress = FreeAddress();
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--startup-child");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(childAddress.ToString());
        using (var child = Process.Start(start)!)
        {
            try
            {
                var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
                Assert(ready == "READY", "Separate launcher starts its API");
                child.Kill(entireProcessTree: false);
                await child.WaitForExitAsync();
                await WaitUntilOfflineAsync(http, childAddress);
                Assert(true, "API stops after forced parent termination in IDE");
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
        Console.WriteLine($"PASS: {checks} checks (local API startup + PostgreSQL)");
    }

    public static async Task RunChildAsync(string[] args)
    {
        using var local = new LocalApiProcess(args[1], new Uri(args[2]));
        await local.EnsureReadyAsync();
        Console.WriteLine("READY");
        await Task.Delay(Timeout.Infinite);
    }

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Clinic.slnx"))) return d.FullName;
        throw new Exception("Run these checks inside the configured source checkout.");
    }

    private static Uri FreeAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }

    private static async Task WaitUntilOfflineAsync(HttpClient http, Uri address)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!timeout.IsCancellationRequested)
        {
            try { using var response = await http.GetAsync(new Uri(address, "health"), timeout.Token); }
            catch (HttpRequestException) { return; }
            await Task.Delay(100, timeout.Token);
        }
        throw new Exception("Owned API is still running after launcher termination.");
    }
}
