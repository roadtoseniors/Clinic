namespace Clinic.API;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record NamedDto(int Id, string Name);
public record NameInput(string Name);


public record AccountFullDto(
    int Id, string Login, int RoleId, string RoleCode, string RoleName,
    int? EmployeeId, int? PatientId, bool IsActive, string UserTheme, DateTime? LastLogin);

public record LoginResponse(AccountFullDto UserAccount, string Token, IReadOnlyList<string> Permissions);
public record MeDto(AccountFullDto Account, IReadOnlyList<string> Permissions);

public record RegisterPatientDto(
    string Login, string Password,
    string LastName, string FirstName, string? MiddleName,
    DateOnly BirthDate, char Gender, string? Snils, string? Address, string? Phone,
    string OmsNumber, int OmsCompanyId);

public record ThemeDto(string Theme);
public record ChangePasswordDto(string OldPassword, string NewPassword);

public record RoomDto(int Id, string Number, short Floor);
public record RoomInput(string Number, short Floor);

public record ServiceDto(int Id, int GroupId, string GroupName, string Name, decimal Price, bool IsActive);
public record ServiceInput(int GroupId, string Name, decimal Price, bool IsActive);

public record DiagnosisDto(string Code, string Name);

public record PayMethodDto(int Id, string Name, bool IsInsurance);
public record PayMethodInput(string Name, bool IsInsurance);

public record InsuranceCompanyDto(int Id, string Name, string? Phone, bool IsActive);
public record InsuranceCompanyInput(string Name, string? Phone, bool IsActive);

public record DistrictDto(int Id, string Number, string Name);
public record DistrictInput(string Number, string Name);

public record EmployeeDto(
    int Id, string LastName, string FirstName, string? MiddleName,
    int PostId, string PostName, int? SpecialtyId, string? SpecialtyName,
    string? Phone, bool IsActive);

public record EmployeeInput(
    string LastName, string FirstName, string? MiddleName,
    int PostId, int? SpecialtyId, string? Phone, bool IsActive);

public record DoctorDto(int Id, string FullName, int SpecialtyId, string SpecialtyName);

public record PatientDto(
    int Id, string CardNumber, string LastName, string FirstName, string? MiddleName,
    DateOnly BirthDate, char Gender, string? Snils, string? Address, string? Phone,
    DateOnly CreatedDate, bool IsActive);

public record PatientInput(
    string LastName, string FirstName, string? MiddleName,
    DateOnly BirthDate, char Gender, string? Snils, string? Address, string? Phone);

public record CreatePatientDto(
    string LastName, string FirstName, string? MiddleName,
    DateOnly BirthDate, char Gender, string? Snils, string? Address, string? Phone,
    string? OmsNumber, int? OmsCompanyId);

public record PatientProfileDto(PatientDto Patient, IReadOnlyList<PolicyDto> Policies, AttachmentDto? Attachment);

public record PolicyDto(
    int Id, int PatientId, string Kind, string Number, int CompanyId, string CompanyName,
    DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive);

public record PolicyInput(string Kind, string Number, int CompanyId, DateOnly ValidFrom, DateOnly? ValidTo);
public record PolicyUpdateInput(string Number, int CompanyId, DateOnly ValidFrom, DateOnly? ValidTo);

public record AttachmentDto(
    int Id, int PatientId, int DistrictId, string DistrictNumber,
    int EmployeeId, string DoctorName, DateOnly AttachedAt, DateOnly? DetachedAt);

public record AttachmentInput(int DistrictId, int EmployeeId);

public record DocumentDto(
    int Id, string Number, int TypeId, string TypeName, int PatientId, int? VisitId,
    DateTime IssuedAt, string? Content);

public record DocumentInput(int TypeId, int? VisitId, string? Content);

public record NewAccountInput(string Login, string Password);


public record ScheduleDto(
    int Id, int EmployeeId, string DoctorName, int RoomId, string RoomNumber,
    DateOnly WorkDate, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes);

public record ScheduleInput(
    int EmployeeId, int RoomId, DateOnly WorkDate,
    TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes);

public record SlotDto(int ScheduleId, int EmployeeId, string DoctorName, DateOnly WorkDate, TimeOnly StartTime);

public record AppointmentDto(
    int Id, int ScheduleId, DateOnly WorkDate, TimeOnly StartTime,
    int PatientId, string PatientName, int EmployeeId, string DoctorName,
    string Status, string Source, DateTime CreatedAt);

public record MyAppointmentDto(
    int Id, int ScheduleId, DateOnly WorkDate, TimeOnly StartTime, string Status,
    int EmployeeId, string DoctorName, string? Specialty, string Room);

public record CreateAppointmentDto(int ScheduleId, TimeOnly StartTime, int? PatientId, string? Source);
public record RescheduleDto(int ScheduleId, TimeOnly StartTime);

public record DoctorDayDto(
    int ScheduleId, DateOnly WorkDate, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes,
    string RoomNumber, IReadOnlyList<DoctorAppointmentDto> Appointments);

public record DoctorAppointmentDto(
    int Id, TimeOnly StartTime, int PatientId, string PatientName, string CardNumber,
    DateOnly BirthDate, string Status, int? VisitId, bool? VisitClosed);

public record VisitDto(
    int Id, int AppointmentId, int PatientId, DateTime VisitedAt,
    string? Complaints, string? Anamnesis, string? ObjectiveStatus,
    string? DiagnosisCode, string? Recommendations, bool IsClosed);

public record VisitDetailsDto(
    int Id, int AppointmentId, int PatientId, DateTime VisitedAt,
    int EmployeeId, string DoctorName, string? Specialty,
    string? Complaints, string? Anamnesis, string? ObjectiveStatus,
    string? DiagnosisCode, string? DiagnosisName, string? Recommendations, bool IsClosed,
    IReadOnlyList<PrescriptionDto> Prescriptions, IReadOnlyList<ReferralDto> Referrals);

public record CreateVisitDto(int AppointmentId);

public record VisitUpdateDto(
    string? Complaints, string? Anamnesis, string? ObjectiveStatus,
    string? DiagnosisCode, string? Recommendations);

public record PrescriptionDto(
    int Id, int VisitId, int DrugId, string DrugName,
    string? Dosage, string? Instruction, bool IsRecipe);

public record PrescriptionInput(int DrugId, string? Dosage, string? Instruction, bool IsRecipe);

public record MedcardDto(PatientProfileDto Profile, IReadOnlyList<VisitDetailsDto> Visits);

public record ReferralDto(
    int Id, int VisitId, int PatientId, string PatientName,
    int ServiceId, string ServiceName, string Status,
    int? ExecutorId, string? ExecutorName, DateTime? DoneAt, string? Result, int FileCount);

public record ReferralInput(int ServiceId);
public record ReferralResultDto(string Result);

public record ReferralFileDto(int Id, int ReferralId, string FileName, string ContentType, long Size, DateTime UploadedAt);

public record VisitServiceDto(
    int Id, int VisitId, int ServiceId, string ServiceName, int? InvoiceId,
    int Quantity, decimal Price, decimal Sum, DateTime RenderedAt);

public record VisitServiceInput(int ServiceId, int Quantity);

public record InvoiceDto(
    int Id, int PatientId, string PatientName, DateTime CreatedAt, string Status,
    int? MethodId, string? MethodName, int? PolicyId, DateTime? PaidAt, decimal Total);

public record InvoiceDetailsDto(InvoiceDto Invoice, IReadOnlyList<VisitServiceDto> Lines);


public record CreateInvoiceDto(int PatientId, IReadOnlyList<int>? VisitServiceIds);
public record PayInvoiceDto(int MethodId, int? PolicyId);

public record NotificationDto(int Id, DateTime CreatedAt, string Message, bool IsRead);

public record AccountDto(
    int Id, string Login, int RoleId, string RoleCode, string RoleName,
    int? EmployeeId, int? PatientId, string? OwnerName, bool IsActive, DateTime? LastLogin);

public record CreateAccountDto(string Login, string Password, int RoleId, int? EmployeeId, int? PatientId);
public record UpdateAccountDto(int RoleId, bool IsActive);
public record ResetPasswordDto(string NewPassword);

public record RoleDto(int Id, string Code, string Name, IReadOnlyList<string> Permissions);
public record PermissionDto(string Code, string Name);
public record RolePermissionsInput(IReadOnlyList<string> Codes);

public record AuditDto(
    long Id, int AccountId, string Login, int? PatientId, DateTime ActedAt,
    string Action, string TableName, int? RecordId);

public record AttendanceRow(DateOnly Date, int Total, int Planned, int Completed, int NoShow, int Cancelled);
public record DoctorLoadRow(int EmployeeId, string DoctorName, int Days, int TotalSlots, int Booked, decimal LoadPercent);
public record MorbidityRow(string DiagnosisCode, string DiagnosisName, int Visits, int Patients);
public record RevenueRow(DateOnly Date, int Invoices, decimal Amount);


public record BackupFileDto(string FileName, long Size, DateTime CreatedAt);
public record BackupLogDto(int Id, int AccountId, string Action, string FileName, DateTime CreatedAt);
public record RestoreDto(string FileName);
