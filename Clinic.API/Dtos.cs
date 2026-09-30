namespace Clinic.API;
//Data Transfer Object
public record AccountDto(string Login);
public record LoginResponse(AccountDto UserAccount, string Token);

public record RoleDto(int Id, string Name);
public record PostDto(int Id, string Name);
public record SpecialtyDto(int Id, string Name);
public record PayMethodDto(int Id, string Name);
public record ServiceGroupDto(int Id, string Name);
public record DiagnosisDto(string Code, string Name);
public record DrugDto(int Id, string Name);
public record RoomDto(int Id, string Number, short Floor);

public record AccountFullDto(
    int Id,
    string Login,
    int RoleId,
    int? EmployeeId,
    int? PatientId,
    bool IsActive,
    DateTime? LastLogin);
    
public record EmployeeDto(
    int Id,
    string LastName,
    string FirstName,
    string? MiddleName,
    int PostId,
    string PostName,
    int? SpecialtyId,
    string? SpecialtyName,
    string? Phone,
    bool IsActive);

public record ScheduleDto(
    int Id,
    int EmployeeId,
    int RoomId,
    DateOnly WorkDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotMinutes);
    
public record PatientDto(
    int Id,
    string CardNumber,
    string LastName,
    string FirstName,
    string? MiddleName,
    DateOnly BirthDate,
    char Gender,
    string? Snils,
    string OmsPolicy,
    string? DmsPolicy,
    string? Address,
    string? Phone,
    DateOnly CreatedDate,
    bool IsActive);
    
public record AppointmentDto(
    int Id,
    int ScheduleId,
    DateOnly WorkDate,
    TimeOnly StartTime,
    int PatientId,
    string Status,
    string Source,
    DateTime CreatedAt);

public record VisitDto(
    int Id,
    int AppointmentId,
    DateTime VisitedAt,
    string? Complaints,
    string? Anamnesis,
    string? ObjectiveStatus,
    string? DiagnosisCode,
    string? Recommendations,
    bool IsClosed);

public record PrescriptionDto(
    int Id,
    int VisitId,
    int DrugId,
    string? Dosage,
    string? Instruction,
    bool IsRecipe);

public record ReferralDto(
    int Id,
    int VisitId,
    int ServiceId,
    string Status,
    int? ExecutorId,
    DateTime? DoneAt,
    string? Result);
    
public record ServiceDto(
    int Id,
    int GroupId,
    string Name,
    decimal Price,
    bool IsActive);

public record VisitServiceDto(
    int Id,
    int VisitId,
    int ServiceId,
    int? InvoiceId,
    int Quantity,
    decimal Price,
    DateTime RenderedAt);

public record InvoiceDto(
    int Id,
    int PatientId,
    DateTime CreatedAt,
    string Status,
    int? MethodId,
    DateTime? PaidAt,
    decimal Total);
    
public record AuditDto(
    long Id,
    int AccountId,
    int? PatientId,
    DateTime ActedAt,
    string Action,
    string TableName,
    int? RecordId);