using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway
{
    private static string FieldValue(Dictionary<string, string> values, string key)
    {
        return values.GetValueOrDefault(key, "").Trim();
    }

    private static int Number(Dictionary<string, string> values, string key)
    {
        if (int.TryParse(FieldValue(values, key), out var number))
        {
            return number;
        }

        throw new InvalidOperationException("Заполните поле: " + key);
    }

    private static int? OptionalNumber(Dictionary<string, string> values, string key)
    {
        if (FieldValue(values, key) == "")
        {
            return null;
        }

        return Number(values, key);
    }

    private static string? OptionalText(Dictionary<string, string> values, string key)
    {
        var value = FieldValue(values, key);
        return value == "" ? null : value;
    }

    private static object PersonData(Dictionary<string, string> values)
    {
        return new
        {
            lastName = FieldValue(values, "last_name"),
            firstName = FieldValue(values, "first_name"),
            middleName = OptionalText(values, "middle_name"),
            birthDate = FieldValue(values, "birth_date"),
            gender = FieldValue(values, "gender"),
            snils = OptionalText(values, "snils"),
            address = OptionalText(values, "address"),
            phone = OptionalText(values, "phone")
        };
    }

    public async Task<int> SaveAsync(ClinicSession session, Mutation mutation)
    {
        Check(session);
        var values = mutation.Values;
        var id = mutation.Id;
        var method = id == 0 ? "POST" : "PUT";
        JsonElement result;
        foreach (var field in FieldsFor(mutation.Kind, id))
        {
            if (field.Required && FieldValue(values, field.Key) == "")
            {
                throw new InvalidOperationException("Заполните «" + field.Label + "».");
            }
        }

        switch (mutation.Kind)
        {
            case "role_permissions":
                var permissionCodes = values
                    .Where(value => value.Key.StartsWith("perm:") && value.Value == "true")
                    .Select(value => value.Key[5..])
                    .ToArray();
                await Request("PUT", "api/roles/" + id + "/permissions", new { codes = permissionCodes });
                return id;
            case "password":
                if (values.GetValueOrDefault("new_password", "") != values.GetValueOrDefault("confirmation", ""))
                {
                    throw new InvalidOperationException("Пароли не совпадают.");
                }

                await ChangePasswordAsync(values["old_password"], values["new_password"]);
                return session.AccountId;
            case "patient":
                var patientPath = id == 0 ? "api/patients/details" : "api/patients/" + id + "/details";
                result = await Request(method, patientPath, new
                {
                    patient = PersonData(values),
                    omsNumber = FieldValue(values, "oms_policy"),
                    omsCompanyId = Number(values, "oms_company_id"),
                    dmsNumber = OptionalText(values, "dms_policy"),
                    dmsCompanyId = OptionalNumber(values, "dms_company_id"),
                    districtId = OptionalNumber(values, "district_id"),
                    employeeId = OptionalNumber(values, "primary_doctor_id")
                });
                break;
            case "schedule":
                var schedulePath = "api/schedules" + (id == 0 ? "" : "/" + id);
                result = await Request(method, schedulePath, new
                {
                    employeeId = Number(values, "employee_id"),
                    roomId = Number(values, "room_id"),
                    workDate = FieldValue(values, "work_date"),
                    startTime = FieldValue(values, "start_time"),
                    endTime = FieldValue(values, "end_time"),
                    slotMinutes = Number(values, "slot_minutes")
                });
                break;
            case "booking":
                var slot = FieldValue(values, "slot").Split('|');
                if (slot.Length != 2)
                {
                    throw new InvalidOperationException("Выберите слот.");
                }

                if (id == 0)
                {
                    result = await Request("POST", "api/appointments", new
                    {
                        scheduleId = int.Parse(slot[0]),
                        startTime = slot[1],
                        patientId = session.Role == "Пациент" ? session.PatientId : OptionalNumber(values, "patient_id"),
                        source = session.Role == "Пациент" ? "приложение" : "регистратура"
                    });
                }
                else
                {
                    result = await Request("POST", "api/appointments/" + id + "/reschedule", new
                    {
                        scheduleId = int.Parse(slot[0]),
                        startTime = slot[1]
                    });
                }

                break;
            case "visit":
                result = await Request("PUT", "api/visits/" + id, new
                {
                    complaints = FieldValue(values, "complaints"),
                    anamnesis = OptionalText(values, "anamnesis"),
                    objectiveStatus = OptionalText(values, "objective_status"),
                    diagnosisCode = OptionalText(values, "diagnosis_code"),
                    recommendations = OptionalText(values, "recommendations")
                });
                break;
            case "prescription":
                var prescriptionPath = id == 0
                    ? "api/visits/" + Number(values, "visit_id") + "/prescriptions"
                    : "api/prescriptions/" + id;
                result = await Request(method, prescriptionPath, new
                {
                    drugId = Number(values, "drug_id"),
                    dosage = FieldValue(values, "dosage"),
                    instruction = FieldValue(values, "instruction"),
                    isRecipe = FieldValue(values, "is_recipe") == "true"
                });
                break;
            case "referral":
                result = await Request("POST", "api/visits/" + Number(values, "visit_id") + "/referrals", new
                {
                    serviceId = Number(values, "service_id")
                });
                break;
            case "result":
                using (var form = new MultipartFormDataContent())
                {
                    form.Add(new StringContent(FieldValue(values, "result")), "result");
                    List<AttachmentUpload> uploads = [];
                    if (values.TryGetValue("files_json", out var filesJson))
                    {
                        uploads = JsonSerializer.Deserialize<List<AttachmentUpload>>(filesJson) ?? [];
                    }
                    else if (values.TryGetValue("file_data", out var data) && data.Length > 0)
                    {
                        uploads.Add(new AttachmentUpload(FieldValue(values, "file_name"), data));
                    }

                    foreach (var upload in uploads)
                    {
                        var bytes = Convert.FromBase64String(upload.Data);
                        if (bytes.Length > 10 * 1024 * 1024)
                        {
                            throw new InvalidOperationException("Максимум 10 МБ.");
                        }

                        var mime = System.IO.Path.GetExtension(upload.Name).ToLowerInvariant() switch
                        {
                            ".pdf" => "application/pdf",
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            _ => throw new InvalidOperationException("Выберите файл PDF, JPG или PNG.")
                        };
                        var content = new ByteArrayContent(bytes);
                        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
                        form.Add(content, "files", upload.Name);
                    }

                    result = await Request("POST", "api/referrals/" + id + "/complete", form);
                }

                break;
            case "rendered":
                var renderedPath = id == 0
                    ? "api/visits/" + Number(values, "visit_id") + "/services"
                    : "api/visit-services/" + id;
                result = await Request(method, renderedPath, new
                {
                    serviceId = Number(values, "service_id"),
                    quantity = Number(values, "quantity")
                });
                break;
            case "payment":
                await Request("POST", "api/invoices/" + id + "/pay", new
                {
                    methodId = Number(values, "method_id"),
                    policyId = OptionalNumber(values, "policy_id")
                });
                return id;
            case "document":
                result = await Request("POST", "api/patients/" + Number(values, "patient_id") + "/documents", new
                {
                    typeId = Number(values, "type_id"),
                    visitId = OptionalNumber(values, "visit_id"),
                    content = FieldValue(values, "content")
                });
                break;
            case "account":
                if (id == 0)
                {
                    result = await Request("POST", "api/accounts", new
                    {
                        login = FieldValue(values, "login"),
                        password = values.GetValueOrDefault("password", ""),
                        roleId = Number(values, "role_id"),
                        employeeId = OptionalNumber(values, "employee_id"),
                        patientId = OptionalNumber(values, "patient_id")
                    });
                }
                else
                {
                    var account = (await Many("api/accounts")).First(item => Integer(item, "id") == id);
                    result = await Request("PUT", "api/accounts/" + id, new
                    {
                        roleId = Number(values, "role_id"),
                        isActive = Boolean(account, "isActive")
                    });
                    if (values.GetValueOrDefault("password", "").Length > 0)
                    {
                        await Request("POST", "api/accounts/" + id + "/reset-password", new
                        {
                            newPassword = values["password"]
                        });
                    }
                }

                break;
            case "employee":
            case "service":
                var active = id == 0;
                if (id != 0 && mutation.Kind == "employee")
                {
                    var employee = await Request("GET", "api/employees/" + id);
                    active = Boolean(employee, "isActive");
                }

                if (id > 0 && mutation.Kind == "service")
                {
                    // У услуг нет отдельного GET по идентификатору.
                    var services = await Many("api/services?includeInactive=true");
                    active = Boolean(services.First(service => Integer(service, "id") == id), "isActive");
                }

                if (mutation.Kind == "employee")
                {
                    result = await Request(method, "api/employees" + (id == 0 ? "" : "/" + id), new
                    {
                        lastName = FieldValue(values, "last_name"),
                        firstName = FieldValue(values, "first_name"),
                        middleName = OptionalText(values, "middle_name"),
                        postId = Number(values, "post_id"),
                        specialtyId = OptionalNumber(values, "specialty_id"),
                        phone = OptionalText(values, "phone"),
                        isActive = active
                    });
                }
                else
                {
                    result = await Request(method, "api/services" + (id == 0 ? "" : "/" + id), new
                    {
                        name = FieldValue(values, "name"),
                        groupId = Number(values, "group_id"),
                        price = decimal.Parse(FieldValue(values, "price").Replace(',', '.'), CultureInfo.InvariantCulture),
                        isActive = active
                    });
                }

                break;
            case "dictionary":
                var table = FieldValue(values, "table");
                if (!DictionaryRoutes.TryGetValue(table, out var route))
                {
                    throw new InvalidOperationException("Неизвестный справочник.");
                }

                object body = table switch
                {
                    "room" => new
                    {
                        number = FieldValue(values, "name"),
                        floor = short.TryParse(FieldValue(values, "code"), out var floor) ? floor : (short)1
                    },
                    "diagnosis" => new
                    {
                        code = FieldValue(values, "code"),
                        name = FieldValue(values, "name")
                    },
                    "district" => new
                    {
                        number = FieldValue(values, "code"),
                        name = FieldValue(values, "name")
                    },
                    "insurance_company" => new
                    {
                        name = FieldValue(values, "name"),
                        phone = (string?)null,
                        isActive = true
                    },
                    "pay_method" => new
                    {
                        name = FieldValue(values, "name"),
                        isInsurance = FieldValue(values, "name") == "ДМС"
                    },
                    _ => new
                    {
                        name = FieldValue(values, "name")
                    }
                };
                if (table == "diagnosis" && id == 0)
                {
                    var diagnoses = await Many("api/diagnoses");
                    if (diagnoses.Any(diagnosis => Text(diagnosis, "code") == FieldValue(values, "code")))
                    {
                        id = -1;
                    }
                }

                var dictionaryPath = "api/" + route;
                if (id != 0)
                {
                    var key = table == "diagnosis" ? FieldValue(values, "code") : id.ToString();
                    dictionaryPath += "/" + Uri.EscapeDataString(key);
                }

                result = await Request(id == 0 ? "POST" : "PUT", dictionaryPath, body);
                break;
            default:
                throw new InvalidOperationException("Операция недоступна.");
        }

        var savedId = Integer(result, "id");
        return savedId > 0 ? savedId : id;
    }

    public async Task ActAsync(ClinicSession session, string action, int id, Dictionary<string, string>? values = null)
    {
        Check(session);
        switch (action)
        {
            case "delete_referral":
                await Request("DELETE", "api/referrals/" + id);
                return;
            case "delete_schedule":
                await Request("DELETE", "api/schedules/" + id);
                return;
            case "delete_prescription":
                await Request("DELETE", "api/prescriptions/" + id);
                return;
            case "delete_rendered":
                await Request("DELETE", "api/visit-services/" + id);
                return;
            case "delete_account":
                await Request("DELETE", "api/accounts/" + id);
                return;
            case "cancel_invoice":
                await Request("POST", "api/invoices/" + id + "/cancel");
                return;
            case "read_all":
                await Request("POST", "api/my/notifications/read-all");
                return;
            case "delete_dictionary":
                var table = values?.GetValueOrDefault("table") ?? "";
                if (!DictionaryRoutes.TryGetValue(table, out var route))
                {
                    throw new InvalidOperationException("Неизвестный справочник");
                }

                var code = table == "diagnosis" ? values!.GetValueOrDefault("code", "") : id.ToString();
                await Request("DELETE", "api/" + route + "/" + Uri.EscapeDataString(code));
                return;
            case "start_visit":
                var visits = await Visits(session);
                if (visits.Any(visit => Integer(visit, "appointmentId") == id))
                {
                    return;
                }

                await Request("POST", "api/visits", new { appointmentId = id });
                return;
            case "close_visit":
                await Request("POST", "api/visits/" + id + "/close");
                return;
            case "cancel":
            case "no_show":
                var appointmentAction = action == "cancel" ? "/cancel" : "/no-show";
                await Request("POST", "api/appointments/" + id + appointmentAction);
                return;
            case "cancel_referral":
                await Request("POST", "api/referrals/" + id + "/cancel");
                return;
            case "mark_read":
                await Request("POST", "api/my/notifications/" + id + "/read");
                return;
            case "create_invoice":
                var service = (await ListAsync(session, "rendered")).First(row => row.Id == id);
                await Request("POST", "api/invoices", new
                {
                    patientId = int.Parse(service.Values["patient_id"]),
                    visitServiceIds = new[] { id }
                });
                return;
            case "toggle_patient":
                var patient = Property(await Request("GET", "api/patients/" + id), "patient");
                if (Boolean(patient, "isActive"))
                {
                    await Request("DELETE", "api/patients/" + id);
                }
                else
                {
                    await Request("POST", "api/patients/" + id + "/restore");
                }

                return;
            case "toggle_account":
                var account = (await Many("api/accounts")).First(item => Integer(item, "id") == id);
                await Request("PUT", "api/accounts/" + id, new
                {
                    roleId = Integer(account, "roleId"),
                    isActive = !Boolean(account, "isActive")
                });
                return;
            case "toggle_employee":
            case "toggle_service":
                var resource = action == "toggle_employee" ? "employees" : "services";
                var records = await Many("api/" + resource + "?includeInactive=true");
                var record = records.First(item => Integer(item, "id") == id);
                if (Boolean(record, "isActive"))
                {
                    await Request("DELETE", "api/" + resource + "/" + id);
                }
                else if (resource == "employees")
                {
                    var specialtyId = Integer(record, "specialtyId");
                    await Request("PUT", "api/employees/" + id, new
                    {
                        lastName = Text(record, "lastName"),
                        firstName = Text(record, "firstName"),
                        middleName = NullableString(record, "middleName"),
                        postId = Integer(record, "postId"),
                        specialtyId = specialtyId == 0 ? (int?)null : specialtyId,
                        phone = NullableString(record, "phone"),
                        isActive = true
                    });
                }
                else
                {
                    await Request("PUT", "api/services/" + id, new
                    {
                        name = Text(record, "name"),
                        groupId = Integer(record, "groupId"),
                        price = decimal.Parse(Text(record, "price"), CultureInfo.InvariantCulture),
                        isActive = true
                    });
                }

                return;
            default:
                throw new InvalidOperationException("Действие недоступно.");
        }
    }

    private static string? NullableString(JsonElement row, string key)
    {
        var value = Text(row, key);
        return value.Length == 0 ? null : value;
    }

    public async Task RegisterAsync(Dictionary<string, string> values)
    {
        if (values.GetValueOrDefault("password", "") != values.GetValueOrDefault("password_confirmation", ""))
        {
            throw new InvalidOperationException("Пароли не совпадают.");
        }

        await Request("POST", "auth/register", new
        {
            login = FieldValue(values, "login"),
            password = values.GetValueOrDefault("password", ""),
            lastName = FieldValue(values, "last_name"),
            firstName = FieldValue(values, "first_name"),
            middleName = OptionalText(values, "middle_name"),
            birthDate = FieldValue(values, "birth_date"),
            gender = FieldValue(values, "gender"),
            snils = OptionalText(values, "snils"),
            address = OptionalText(values, "address"),
            phone = OptionalText(values, "phone"),
            omsNumber = FieldValue(values, "oms_policy"),
            omsCompanyId = Number(values, "oms_company_id")
        });
    }

    public async Task<List<AttachmentInfo>> AttachmentsAsync(ClinicSession session, int referralId)
    {
        Check(session);
        var path = session.Role == "Пациент"
            ? "api/my/referrals/" + referralId + "/files"
            : "api/referrals/" + referralId + "/files";
        var files = await Many(path);
        return files.Select(file => new AttachmentInfo(
            Integer(file, "id"),
            Text(file, "fileName"),
            long.TryParse(Text(file, "size"), out var size) ? size : 0)).ToList();
    }

    public async Task<byte[]> DownloadAttachmentAsync(ClinicSession session, int fileId)
    {
        Check(session);
        var path = session.Role == "Пациент" ? "api/my/referral-files/" : "api/referral-files/";
        using var response = await _http.GetAsync(path + fileId);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Не удалось скачать файл. Обновите список.");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]?> AttachmentAsync(ClinicSession session, int referralId)
    {
        var files = await AttachmentsAsync(session, referralId);
        if (files.Count == 0)
        {
            return null;
        }

        return await DownloadAttachmentAsync(session, files[0].Id);
    }

    public async Task DeleteAttachmentAsync(ClinicSession session, int id)
    {
        Check(session);
        await Request("DELETE", "api/referral-files/" + id);
    }

    public async Task<string> BackupAsync(ClinicSession session)
    {
        Check(session);
        var backup = await Request("POST", "api/backups");
        var marker = new
        {
            kind = "clinic-server-backup",
            fileName = Text(backup, "fileName"),
            server = _http.BaseAddress!.ToString(),
            createdAt = Text(backup, "createdAt")
        };
        return JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task RestoreAsync(ClinicSession session, string json)
    {
        Check(session);
        var marker = JsonDocument.Parse(json).RootElement;
        if (Text(marker, "kind") != "clinic-server-backup" || Text(marker, "server") != _http.BaseAddress!.ToString())
        {
            throw new InvalidOperationException("Эта копия относится к другой системе. Выберите копию текущей поликлиники.");
        }

        await Request("POST", "api/backups/restore", new { fileName = Text(marker, "fileName") });
        SessionExpired?.Invoke("Данные восстановлены. Войдите снова.");
    }
}
