namespace Clinic.Data;

public sealed record DashboardMetric(string Value, string Label, string Detail, string Accent);
public sealed record DashboardItem(string Time, string Title, string Detail, string Badge)
{
    public bool HasTime => Time.Length > 0;
    public bool IsSuccessStatus => Clinic.Workspace.StatusPresentation.Tone(Badge) == "success";
    public bool IsErrorStatus => Clinic.Workspace.StatusPresentation.Tone(Badge) == "error";
    public bool IsWarningStatus => Clinic.Workspace.StatusPresentation.Tone(Badge) == "warning";
    public bool IsNeutralStatus => Clinic.Workspace.StatusPresentation.Tone(Badge) == "neutral";
}
