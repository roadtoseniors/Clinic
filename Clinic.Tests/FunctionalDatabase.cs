using Npgsql;
using System.Diagnostics;
using System.Text.Json;
using Clinic.Context;
using Microsoft.EntityFrameworkCore;

internal sealed class FunctionalDatabase : IAsyncDisposable
{
    public string Connection { get; }
    private readonly string _owner;
    private readonly string _name;
    private readonly string _backupFolder;
    private FunctionalDatabase(string connection,string owner,string name,string backupFolder) {Connection=connection;_owner=owner;_name=name;_backupFolder=backupFolder;}
    public static async Task<FunctionalDatabase> CreateAsync(string root)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"Clinic.API","api.local.json")));
        var source=new NpgsqlConnectionStringBuilder(settings.RootElement.GetProperty("ConnectionStrings").GetProperty("Clinic").GetString());
        if(source.Database!="clinic_api_integration_test"||source.Host is not ("localhost" or "127.0.0.1"))throw new Exception("Functional checks require local clinic_api_integration_test as source");
        var name="clinic_functional_"+Guid.NewGuid().ToString("N")[..12];
        var owner=new NpgsqlConnectionStringBuilder(source.ConnectionString){Database="postgres"};
        var target=new NpgsqlConnectionStringBuilder(source.ConnectionString){Database=name};
        var folder=settings.RootElement.TryGetProperty("Backup",out var backup)&&backup.TryGetProperty("Folder",out var configured)?configured.GetString()!:"Backups";
        var result=new FunctionalDatabase(target.ConnectionString,owner.ConnectionString,name,Path.GetFullPath(folder,Path.Combine(root,"Clinic.API")));
        var path=Path.Combine(Path.GetTempPath(),name+".backup");
        try
        {
            await using(var c=new NpgsqlConnection(owner.ConnectionString)) {await c.OpenAsync();await using var cmd=new NpgsqlCommand("CREATE DATABASE "+name,c);await cmd.ExecuteNonQueryAsync();}
            await RunAsync("pg_dump",source,["--format=c","--file",path,"--dbname",source.Database]);
            await RunAsync("pg_restore",target,["--no-owner","--dbname",name,path]);
            return result;
        }
        catch {await result.DisposeAsync();throw;}
        finally {if(File.Exists(path))File.Delete(path);}
    }
    public MyDbContext Context()=>new(new DbContextOptionsBuilder<MyDbContext>().UseNpgsql(Connection).Options);
    public async ValueTask DisposeAsync()
    {
        if(!_name.StartsWith("clinic_functional_",StringComparison.Ordinal))throw new Exception("Unexpected temporary database name");
        if(Directory.Exists(_backupFolder))foreach(var file in Directory.GetFiles(_backupFolder,_name+"_*.backup"))File.Delete(file);
        NpgsqlConnection.ClearAllPools();
        await using var c=new NpgsqlConnection(_owner);await c.OpenAsync();
        await using var cmd=new NpgsqlCommand("DROP DATABASE IF EXISTS "+_name+" WITH (FORCE)",c);await cmd.ExecuteNonQueryAsync();
    }
    private static async Task RunAsync(string program,NpgsqlConnectionStringBuilder cs,string[] arguments)
    {
        var absolute="/opt/homebrew/bin/"+program;
        var info=new ProcessStartInfo(File.Exists(absolute)?absolute:program){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"--host",cs.Host!,"--port",cs.Port.ToString(),"--username",string.IsNullOrWhiteSpace(cs.Username)?Environment.UserName:cs.Username!,"--no-password"}.Concat(arguments))info.ArgumentList.Add(arg);
        info.Environment["PGPASSWORD"]=cs.Password??"";
        using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        try {await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));}
        catch(TimeoutException){p.Kill(entireProcessTree:true);await p.WaitForExitAsync();throw;}
        await output;
        if(p.ExitCode!=0)throw new Exception(program+" failed: "+await error);
    }
}
