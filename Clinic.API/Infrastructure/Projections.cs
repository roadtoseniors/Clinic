using System.Linq.Expressions;

namespace Clinic.API;

// Готовые преобразования «сущность → DTO» для .Select(...).
// EF Core превращает их в SQL, поэтому из базы берутся только нужные колонки (с JOIN по связям).
// Внутри выражений нельзя вызывать свои методы, поэтому ФИО собирается конкатенацией.
public static class Projections
{
    public static readonly Expression<Func<Account, AccountFullDto>> AccountsFull = a => new AccountFullDto(
        a.Id, a.Login, a.RoleId, a.Role.Code, a.Role.Name,
        a.EmployeeId, a.PatientId, a.IsActive, a.UserTheme, a.LastLogin);

    public static readonly Expression<Func<Account, AccountDto>> Accounts = a => new AccountDto(
        a.Id, a.Login, a.RoleId, a.Role.Code, a.Role.Name, a.EmployeeId, a.PatientId,
        a.Employee != null ? a.Employee.LastName + " " + a.Employee.FirstName
            : a.Patient != null ? a.Patient.LastName + " " + a.Patient.FirstName
            : null,
        a.IsActive, a.LastLogin);

    public static readonly Expression<Func<Employee, EmployeeDto>> Employees = e => new EmployeeDto(
        e.Id, e.LastName, e.FirstName, e.MiddleName,
        e.PostId, e.Post.Name,
        e.SpecialtyId, e.Specialty != null ? e.Specialty.Name : null,
        e.Phone, e.IsActive);

    public static readonly Expression<Func<Patient, PatientDto>> Patients = p => new PatientDto(
        p.Id, p.CardNumber, p.LastName, p.FirstName, p.MiddleName,
        p.BirthDate, p.Gender, p.Snils, p.Address, p.Phone,
        p.CreatedDate, p.IsActive);

    public static readonly Expression<Func<PatientPolicy, PolicyDto>> Policies = p => new PolicyDto(
        p.Id, p.PatientId, p.Kind, p.Number, p.CompanyId, p.Company.Name,
        p.ValidFrom, p.ValidTo, p.IsActive);

    public static readonly Expression<Func<PatientAttachment, AttachmentDto>> Attachments = a => new AttachmentDto(
        a.Id, a.PatientId, a.DistrictId, a.District.Number,
        a.EmployeeId, a.Employee.LastName + " " + a.Employee.FirstName,
        a.AttachedAt, a.DetachedAt);

    public static readonly Expression<Func<Document, DocumentDto>> Documents = d => new DocumentDto(
        d.Id, d.Number, d.TypeId, d.Type.Name, d.PatientId, d.VisitId, d.IssuedAt, d.Content);

    public static readonly Expression<Func<Schedule, ScheduleDto>> Schedules = s => new ScheduleDto(
        s.Id, s.EmployeeId, s.Employee.LastName + " " + s.Employee.FirstName,
        s.RoomId, s.Room.Number, s.WorkDate, s.StartTime, s.EndTime, s.SlotMinutes);

    public static readonly Expression<Func<Appointment, AppointmentDto>> Appointments = a => new AppointmentDto(
        a.Id, a.ScheduleId, a.Schedule.WorkDate, a.StartTime,
        a.PatientId, a.Patient.LastName + " " + a.Patient.FirstName,
        a.Schedule.EmployeeId, a.Schedule.Employee.LastName + " " + a.Schedule.Employee.FirstName,
        a.Status, a.Source, a.CreatedAt);

    public static readonly Expression<Func<Appointment, MyAppointmentDto>> MyAppointments = a => new MyAppointmentDto(
        a.Id, a.ScheduleId, a.Schedule.WorkDate, a.StartTime, a.Status,
        a.Schedule.EmployeeId, a.Schedule.Employee.LastName + " " + a.Schedule.Employee.FirstName,
        a.Schedule.Employee.Specialty != null ? a.Schedule.Employee.Specialty.Name : null,
        a.Schedule.Room.Number);

    public static readonly Expression<Func<Visit, VisitDto>> Visits = v => new VisitDto(
        v.Id, v.AppointmentId, v.Appointment.PatientId, v.VisitedAt,
        v.Complaints, v.Anamnesis, v.ObjectiveStatus,
        v.DiagnosisCode, v.Recommendations, v.IsClosed);

    // Полная карточка приёма: с назначениями и направлениями.
    // Название диагноза дозаполняет MedcardQueries отдельным запросом.
    public static readonly Expression<Func<Visit, VisitDetailsDto>> VisitDetails = v => new VisitDetailsDto(
        v.Id, v.AppointmentId, v.Appointment.PatientId, v.VisitedAt,
        v.Appointment.Schedule.EmployeeId,
        v.Appointment.Schedule.Employee.LastName + " " + v.Appointment.Schedule.Employee.FirstName,
        v.Appointment.Schedule.Employee.Specialty != null ? v.Appointment.Schedule.Employee.Specialty.Name : null,
        v.Complaints, v.Anamnesis, v.ObjectiveStatus,
        v.DiagnosisCode, null, v.Recommendations, v.IsClosed,
        v.Prescriptions
            .OrderBy(p => p.Id)
            .Select(p => new PrescriptionDto(p.Id, p.VisitId, p.DrugId, p.Drug.Name, p.Dosage, p.Instruction, p.IsRecipe))
            .ToList(),
        v.Referrals
            .OrderBy(r => r.Id)
            .Select(r => new ReferralDto(
                r.Id, r.VisitId, v.Appointment.PatientId,
                v.Appointment.Patient.LastName + " " + v.Appointment.Patient.FirstName,
                r.ServiceId, r.Service.Name, r.Status, r.ExecutorId,
                r.Executor != null ? r.Executor.LastName + " " + r.Executor.FirstName : null,
                r.DoneAt, r.Result, r.ReferralFiles.Count()))
            .ToList());

    public static readonly Expression<Func<Prescription, PrescriptionDto>> Prescriptions = p => new PrescriptionDto(
        p.Id, p.VisitId, p.DrugId, p.Drug.Name, p.Dosage, p.Instruction, p.IsRecipe);

    public static readonly Expression<Func<Referral, ReferralDto>> Referrals = r => new ReferralDto(
        r.Id, r.VisitId, r.Visit.Appointment.PatientId,
        r.Visit.Appointment.Patient.LastName + " " + r.Visit.Appointment.Patient.FirstName,
        r.ServiceId, r.Service.Name, r.Status, r.ExecutorId,
        r.Executor != null ? r.Executor.LastName + " " + r.Executor.FirstName : null,
        r.DoneAt, r.Result, r.ReferralFiles.Count());

    public static readonly Expression<Func<ReferralFile, ReferralFileDto>> ReferralFiles = f => new ReferralFileDto(
        f.Id, f.ReferralId, f.FileName, f.ContentType, (long)f.Data.Length, f.UploadedAt);

    public static readonly Expression<Func<VisitService, VisitServiceDto>> VisitServices = vs => new VisitServiceDto(
        vs.Id, vs.VisitId, vs.ServiceId, vs.Service.Name, vs.InvoiceId,
        vs.Quantity, vs.Price, vs.Price * vs.Quantity, vs.RenderedAt);

    public static readonly Expression<Func<Invoice, InvoiceDto>> Invoices = i => new InvoiceDto(
        i.Id, i.PatientId, i.Patient.LastName + " " + i.Patient.FirstName,
        i.CreatedAt, i.Status,
        i.MethodId, i.Method != null ? i.Method.Name : null,
        i.PolicyId, i.PaidAt,
        i.VisitServices.Sum(vs => vs.Price * vs.Quantity));

    public static readonly Expression<Func<Notification, NotificationDto>> Notifications = n => new NotificationDto(
        n.Id, n.CreatedAt, n.Message, n.IsRead);

    public static readonly Expression<Func<Audit, AuditDto>> Audits = a => new AuditDto(
        a.Id, a.AccountId, a.Account.Login, a.PatientId, a.ActedAt, a.Action, a.TableName, a.RecordId);
}
