using System.Collections.Generic;
using System.Linq;
namespace Clinic.Workspace;
public static class AccessPresentation
{
    public static bool Read(string role,string page,IReadOnlyList<string> permissions) => page=="home" || Required(role,page).Any(p=>p=="staff"?role!="Пациент":permissions.Contains(p));
    private static string[] Required(string role,string page) => role=="Пациент" && page!="slots" ? ["own.view"] : page switch {
        "slots"=>["own.book","appointment.manage"], "patients"=>["patient.view"],"schedule"=>["staff"],
        "appointments" when role=="Врач"=>["doctor.schedule"], "appointments"=>["appointment.manage"],
        "visits"=>["medcard.view","service.render","invoice.manage"], "prescriptions"=>["medcard.view"],
        "referrals"=>["referral.view"],"rendered"=>["service.render"],"invoices"=>["invoice.manage"],
        "documents"=>["patient.view","medcard.view"],"accounts"=>["account.manage"],"employees"=>["staff"],
        "reports"=>["report.view"],"audit"=>["audit.view"],"backup"=>["backup.manage"],"roles"=>["role.manage"],
        "services" or "dictionary"=>["reference.manage"], _=>[] };
    public static bool Write(string role,string? kind,IReadOnlyList<string> permissions)=>Any(permissions,kind switch {
        "patient"=>["patient.manage"],"booking"=>["own.book","appointment.manage"],"schedule"=>["schedule.manage"],
        "visit" or "prescription"=>["visit.manage"],"referral"=>["referral.issue"],"result"=>["referral.result"],
        "rendered"=>["service.render"],"payment"=>["invoice.manage"],"document"=>["document.manage","visit.manage"],
        "account"=>["account.manage"],"employee"=>["reference.manage","account.manage"],"service" or "dictionary"=>["reference.manage"],
        "role_permissions"=>["role.manage"],"password"=>["signedin"],_=>[] });
    private static bool Any(IReadOnlyList<string> permissions,string[] required)=>required.Any(p=>p=="signedin"||permissions.Contains(p));
    public static bool Action(string role,string? kind,string action,IReadOnlyList<string> permissions)=>action switch {
        "edit"=>Write(role,kind,permissions),"history"=>Any(permissions,["own.view","medcard.view"]),
        "book_slot" or "reschedule" or "cancel"=>Any(permissions,["own.book","appointment.manage"]),
        "start_visit" or "close_visit" or "new_prescription" or "delete_prescription"=>permissions.Contains("visit.manage"),
        "no_show"=>Any(permissions,["visit.manage","appointment.manage"]),
        "new_referral" or "cancel_referral" or "delete_referral"=>permissions.Contains("referral.issue"),
        "result"=>permissions.Contains("referral.result"),"new_document"=>Any(permissions,["document.manage","visit.manage"]),
        "payment" or "create_invoice" or "cancel_invoice"=>permissions.Contains("invoice.manage"),
        "delete_rendered"=>permissions.Contains("service.render"),"delete_schedule"=>permissions.Contains("schedule.manage"),
        "toggle_patient"=>permissions.Contains("patient.manage"),"toggle_account" or "delete_account"=>permissions.Contains("account.manage"),
        "toggle_employee"=>Any(permissions,["reference.manage","account.manage"]),"toggle_service" or "delete_dictionary"=>permissions.Contains("reference.manage"),
        "edit_permissions"=>permissions.Contains("role.manage"),_=>true };
}
