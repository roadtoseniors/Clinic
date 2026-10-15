namespace Clinic.Data;

public sealed record ClinicSession(int AccountId, int? EmployeeId, int? PatientId, string Name, string Role);
public sealed record LiveDashboard(DashboardMetric[] Metrics, DashboardItem[] Items, string SectionTitle, string Note);
