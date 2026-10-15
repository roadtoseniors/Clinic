using Microsoft.AspNetCore.Http;
namespace Clinic.API;
public sealed class ChangeAuditFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || http.Items.ContainsKey("clinic.auditWritten") ||
            result is IStatusCodeHttpResult { StatusCode: >= 300 }) return result;
        var segments = http.Request.Path.Value!.Trim('/').Split('/');
        if (segments.Length < 2) return result;
        var route = segments.Length > 3 && segments[1]=="visits" && segments[3]=="services" ? "visit-services" : segments[1];
        var table = route switch {
            "patients" or "policies" => "patient", "schedules" => "schedule", "appointments" => "appointment", "visits" => "visit",
            "referrals" => "referral", "referral-files" => "referral_file", "prescriptions" => "prescription", "visit-services" => "visit_service",
            "invoices" => "invoice", "accounts" or "me" => "account", "employees" => "employee", "roles" => "role", "services" => "service",
            "posts" => "post", "specialties"=>"specialty", "rooms"=>"room", "drugs"=>"drug", "diagnoses"=>"diagnosis",
            "servicegroups"=>"service_group", "insurancecompanies"=>"insurance_company", "districts"=>"district", "paymethods"=>"pay_method",
            "documenttypes"=>"document_type", _ => route
        };
        var routeHasId=http.Request.RouteValues.ContainsKey("id");
        int? id = int.TryParse(http.Request.RouteValues.GetValueOrDefault("id")?.ToString(),out var routeId) ? routeId : null;
        if (result is IValueHttpResult value && value.Value is not null)
        {
            var entity = value.Value.GetType().GetProperty("Invoice")?.GetValue(value.Value) ?? value.Value;
            if (entity.GetType().GetProperty("Id")?.GetValue(entity) is int createdId) id=createdId;
        }
        if(route=="me")id=http.User.GetAccountId();
        var insertion=HttpMethods.IsPost(http.Request.Method) && ((!routeHasId && route!="me") ||
            segments.Length==4 && segments[^1] is "services" or "prescriptions" or "referrals" or "policies" or "attachments" or "files" or "documents");
        var action=HttpMethods.IsDelete(http.Request.Method)?AuditAction.Delete:insertion?AuditAction.Insert:AuditAction.Update;
        await http.RequestServices.GetRequiredService<AuditService>().LogAsync(http.User,action,table,id,table=="patient"?id:null,http.RequestAborted);
        return result;
    }
}
