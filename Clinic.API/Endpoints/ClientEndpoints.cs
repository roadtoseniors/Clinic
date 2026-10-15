namespace Clinic.API;

// Read models for the existing desktop tables; permissions are enforced on the server.
public static class ClientEndpoints
{
    public static void MapClientEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/visits", async (ClaimsPrincipal user,MyDbContext db,int? page,int? pageSize,CancellationToken ct)=>
        {
            var query=db.Visits.AsNoTracking();
            if(user.IsInRole(RoleCode.Doctor))query=query.Where(v=>v.Appointment.Schedule.EmployeeId==user.GetEmployeeId());
            return Results.Ok(await query.OrderByDescending(v=>v.VisitedAt).Select(v=>new
            {
                v.Id,v.AppointmentId,v.VisitedAt,v.IsClosed,
                v.Appointment.PatientId,PatientName=v.Appointment.Patient.LastName+" "+v.Appointment.Patient.FirstName,
                v.Appointment.Schedule.EmployeeId,DoctorName=v.Appointment.Schedule.Employee.LastName+" "+v.Appointment.Schedule.Employee.FirstName
            }).ToPagedAsync(page,pageSize,ct));
        }).RequirePermission(Perm.MedcardView,Perm.ServiceRender,Perm.InvoiceManage);
        api.MapGet("/documents",async(ClaimsPrincipal user,MyDbContext db,CancellationToken ct)=>
        {
            var query=db.Documents.AsNoTracking();
            if(user.IsInRole(RoleCode.Doctor))query=query.Where(d=>d.Patient.Appointments.Any(a=>a.Schedule.EmployeeId==user.GetEmployeeId()));
            return Results.Ok(await query.OrderByDescending(d=>d.IssuedAt).Select(d=>new
            {
                d.Id,d.PatientId,d.VisitId,d.Number,d.TypeId,TypeName=d.Type.Name,d.IssuedAt,d.Content,
                PatientName=d.Patient.LastName+" "+d.Patient.FirstName
            }).ToListAsync(ct));
        }).RequirePermission(Perm.PatientView,Perm.MedcardView);
        api.MapGet("/my/referrals/{id:int}/files",async(int id,ClaimsPrincipal user,MyDbContext db,CancellationToken ct)=>
        {
            var patient=user.GetPatientId();if(patient is null)return Err.Forbidden();
            return Results.Ok(await db.ReferralFiles.AsNoTracking().Where(f=>f.ReferralId==id&&f.Referral.Visit.Appointment.PatientId==patient).Select(Projections.ReferralFiles).ToListAsync(ct));
        }).RequirePermission(Perm.OwnView);
    }
}
