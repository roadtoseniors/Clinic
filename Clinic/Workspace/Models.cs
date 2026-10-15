using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Demo;

namespace Clinic.Workspace;

public sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
public sealed record Section(string Key, string Title, string Icon, string Description, string[] Headers,
    string? EditKind = null, string? NewLabel = null)
{
    public string IconPath => Key switch
    {
        "home" => "M3,10 L12,3 L21,10 M5,9 V21 H10 V14 H14 V21 H19 V9",
        "patients" or "accounts" or "employees" => "M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M4,21 V18 C4,12 20,12 20,18 V21",
        "schedule" or "appointments" or "slots" => "M4,5 H20 V21 H4 Z M8,2 V8 M16,2 V8 M4,10 H20",
        "invoices" or "services" or "rendered" => "M5,3 H19 V21 L16,19 L12,21 L8,19 L5,21 Z M8,8 H16 M8,12 H16 M8,16 H13",
        "reports" => "M3,21 H21 M6,18 V12 M12,18 V7 M18,18 V3",
        "referrals" => "M9,3 H15 M12,3 V8 L19,19 Q20,21 17,21 H7 Q4,21 5,19 L12,8 M8,15 H16",
        "notifications" => "M5,17 H19 L17,14 V8 A5,5 0 0 0 7,8 V14 Z M10,21 H14",
        "prescriptions" => "M12,4 V20 M4,12 H20",
        _ => "M5,3 H15 L20,8 V21 H5 Z M15,3 V8 H20 M8,12 H17 M8,16 H17"
    };
}
public sealed class Record
{
    public int Id { get; init; }
    public string Cell1 { get; init; } = "";
    public string Cell2 { get; init; } = "";
    public string Cell3 { get; init; } = "";
    public string Cell4 { get; init; } = "";
    public string Status { get; init; } = "";
    public string Details { get; init; } = "";
    public bool IsSuccessStatus => StatusPresentation.Tone(Status) == "success";
    public bool IsErrorStatus => StatusPresentation.Tone(Status) == "error";
    public bool IsWarningStatus => StatusPresentation.Tone(Status) == "warning";
    public bool IsNeutralStatus => StatusPresentation.Tone(Status) == "neutral";
    public Dictionary<string,string> Values { get; init; } = new();
}
public sealed record FieldSpec(string Key, string Label, bool Required = false, string? Lookup = null,
    bool Multiline = false, string? Default = null, bool Secret = false);
public sealed record Mutation(string Kind, int Id, Dictionary<string,string> Values);

public interface IClinicGateway
{
    Task<(ClinicSession Session, LiveDashboard Dashboard)?> SignInAsync(string login,string password);
    Task<bool?> GetUserThemeAsync(int accountId);
    Task SaveUserThemeAsync(int accountId,bool dark);
    Task<LiveDashboard> DashboardAsync(ClinicSession session);
    Task<List<Record>> ListAsync(ClinicSession session,string page,DateTime? from=null,DateTime? to=null);
    Task<Dictionary<string,List<Choice>>> ChoicesAsync(ClinicSession session);
    Task<int> SaveAsync(ClinicSession session,Mutation mutation);
    Task ActAsync(ClinicSession session,string action,int id,Dictionary<string,string>? values = null);
    Task RegisterAsync(Dictionary<string,string> values);
    Task<byte[]?> AttachmentAsync(ClinicSession session,int referralId);
    Task<string> BackupAsync(ClinicSession session);
    Task RestoreAsync(ClinicSession session,string json);
}
