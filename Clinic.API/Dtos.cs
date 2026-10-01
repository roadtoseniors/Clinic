namespace Clinic.API;
//Data Transfer Object
// ===== Авторизация =====
public record AccountFullDto(
    int Id, string Login, int RoleId, string RoleCode,
    int? EmployeeId, int? PatientId, bool IsActive, string UserTheme, DateTime? LastLogin);

public record LoginResponse(AccountFullDto UserAccount, string Token, IReadOnlyList<string> Permissions);
public record ThemeDto(string Theme);

// ===== Справочники =====
public record RoleDto(int Id, string Code, string Name);
public record PostDto(int Id, string Name);
public record SpecialtyDto(int Id, string Name);
public record PayMethodDto(int Id, string Name, bool IsInsurance);
public record ServiceGroupDto(int Id, string Name);
public record DiagnosisDto(string Code, string Name);
public record DrugDto(int Id, string Name);
public record RoomDto(int Id, string Number, short Floor);
public record InsuranceCompanyDto(int Id, string Name, string? Phone, bool IsActive);
public record DistrictDto(int Id, string Number, string Name);
public record DocumentTypeDto(int Id, string Name);

public record ServiceDto(int Id, int GroupId, string Name, decimal Price, bool IsActive);

// ===== Сотрудники и расписание =====
public record EmployeeDto(
    int Id, string LastName, string FirstName, string? MiddleName,
    int PostId, string PostName, int? SpecialtyId, string? SpecialtyName,
    string? Phone, bool IsActive);

public record ScheduleDto(
    int Id, int EmployeeId, int RoomId, DateOnly WorkDate,
    TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes);

// ===== Пациенты =====
public record PatientDto(
    int Id, string CardNumber, string LastName, string FirstName, string? MiddleName,
    DateOnly BirthDate, char Gender, string? Snils, string? Address, string? Phone,
    DateOnly CreatedDate, bool IsActive);

public record PolicyDto(
    int Id, int PatientId, string Kind, string Number, int CompanyId, string CompanyName,
    DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive);

public record NotificationDto(int Id, DateTime CreatedAt, string Message, bool IsRead);

// ===== Приёмы =====
public record AppointmentDto(
    int Id, int ScheduleId, DateOnly WorkDate, TimeOnly StartTime,
    int PatientId, string Status, string Source, DateTime CreatedAt);

// для экрана пациента «Мои записи»
public record MyAppointmentDto(
    int Id, DateOnly WorkDate, TimeOnly StartTime, string Status,
    string DoctorName, string? Specialty, string Room);

public record VisitDto(
    int Id, int AppointmentId, DateTime VisitedAt, string? Complaints, string? Anamnesis,
    string? ObjectiveStatus, string? DiagnosisCode, string? Recommendations, bool IsClosed);

public record PrescriptionDto(int Id, int VisitId, int DrugId, string? Dosage, string? Instruction, bool IsRecipe);

public record ReferralDto(
    int Id, int VisitId, int ServiceId, string Status,
    int? ExecutorId, DateTime? DoneAt, string? Result);

// ===== Услуги и счета =====
public record VisitServiceDto(
    int Id, int VisitId, int ServiceId, int? InvoiceId, int Quantity, decimal Price, DateTime RenderedAt);

public record InvoiceDto(
    int Id, int PatientId, DateTime CreatedAt, string Status,
    int? MethodId, int? PolicyId, DateTime? PaidAt, decimal Total);

// ===== Журнал =====
public record AuditDto(
    long Id, int AccountId, int? PatientId, DateTime ActedAt,
    string Action, string TableName, int? RecordId);