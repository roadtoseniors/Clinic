using System;
using System.Collections.Generic;
using System.Linq;

namespace Clinic.Workspace;
public static partial class Catalog
{
    public static FieldSpec[] Fields(string kind) => kind switch
    {
        "patient" =>
        [
            new("last_name", "Фамилия", true),
            new("first_name", "Имя", true),
            new("middle_name", "Отчество"),
            new("birth_date", "Дата рождения (ГГГГ-ММ-ДД)", true),
            new("gender", "Пол", true, "gender"),
            new("oms_policy", "Полис ОМС (16 цифр)", true),
            new("snils", "СНИЛС"),
            new("dms_policy", "Полис ДМС"),
            new("insurance_org", "Страховая организация"),
            new("passport", "Паспорт"),
            new("phone", "Телефон"),
            new("address", "Адрес"),
            new("district", "Участок"),
            new("primary_doctor_id", "Участковый врач", Lookup: "doctors"),
            new("blood_group", "Группа крови"),
            new("allergies", "Аллергии", Multiline: true),
        ],
        "schedule" =>
        [
            new("employee_id", "Врач", true, "doctors"),
            new("room_id", "Кабинет", true, "rooms"),
            new("work_date", "Дата (ГГГГ-ММ-ДД)", true, Default: DateTime.Today.ToString("yyyy-MM-dd")),
            new("start_time", "Начало (ЧЧ:ММ)", true, Default: "09:00"),
            new("end_time", "Окончание (ЧЧ:ММ)", true, Default: "16:00"),
            new("slot_minutes", "Длительность приёма, минут", true, Default: "30"),
        ],
        "booking" =>
        [
            new("patient_id", "Пациент", true, "patients"),
            new("slot", "Свободное время", true, "slots"),
        ],
        "visit" =>
        [
            new("complaints", "Жалобы", true, Multiline: true),
            new("anamnesis", "Анамнез", Multiline: true),
            new("objective_status", "Объективный статус", Multiline: true),
            new("diagnosis_code", "Диагноз МКБ-10", true, "diagnoses"),
            new("recommendations", "Рекомендации", Multiline: true),
        ],
        "prescription" =>
        [
            new("visit_id", "Приём", true, "open_visits"),
            new("drug_id", "Препарат", true, "drugs"),
            new("dosage", "Дозировка", true),
            new("instruction", "Инструкция", true, Multiline: true),
            new("is_recipe", "Рецептурный препарат", true, "boolean", Default: "false"),
            new("valid_until", "Действует до (ГГГГ-ММ-ДД)"),
        ],
        "referral" =>
        [
            new("visit_id", "Приём", true, "open_visits"),
            new("service_id", "Исследование / консультация", true, "services"),
            new("consultation_doctor_id", "Врач для консультации", Lookup: "doctors"),
        ],
        "result" =>
        [
            new("result", "Заключение", true, Multiline: true),
            new("file_name", "Файл результата"),
        ],
        "rendered" =>
        [
            new("visit_id", "Приём", true, "visits"),
            new("service_id", "Услуга", true, "services"),
            new("quantity", "Количество", true, Default: "1"),
        ],
        "payment" =>
        [
            new("amount", "Сумма платежа", true),
            new("method_id", "Способ оплаты", true, "methods"),
        ],
        "document" =>
        [
            new("patient_id", "Пациент", true, "patients"),
            new("kind", "Вид документа", true, "document_types"),
            new("content", "Текст документа", true, Multiline: true),
        ],
        "account" =>
        [
            new("login", "Логин", true),
            new("password", "Новый пароль (пусто — сохранить текущий)", Secret: true),
            new("role_id", "Роль", true, "roles"),
            new("employee_id", "Сотрудник", Lookup: "employees"),
            new("patient_id", "Пациент", Lookup: "patients"),
        ],
        "employee" =>
        [
            new("last_name", "Фамилия", true),
            new("first_name", "Имя", true),
            new("middle_name", "Отчество"),
            new("post_id", "Должность", true, "posts"),
            new("specialty_id", "Специальность", Lookup: "specialties"),
            new("phone", "Телефон"),
        ],
        "service" =>
        [
            new("name", "Название", true),
            new("group_id", "Категория", true, "groups"),
            new("price", "Цена, ₽", true, Default: "0"),
        ],
        "dictionary" =>
        [
            new("table", "Справочник", true, "dictionary_types"),
            new("code", "Код МКБ-10 / этаж кабинета"),
            new("name", "Название / номер кабинета", true),
        ],
        "registration" =>
        [
            new("oms_policy", "Полис ОМС (16 цифр)", true),
            new("birth_date", "Дата рождения (ГГГГ-ММ-ДД)", true),
            new("login", "Логин", true),
            new("password", "Пароль (от 8 символов)", true, Secret: true),
        ],
        "password" =>
        [
            new("old_password", "Текущий пароль", true, Secret: true),
            new("new_password", "Новый пароль (от 8 символов)", true, Secret: true),
            new("confirmation", "Подтверждение нового пароля", true, Secret: true),
        ],
        _ => []
    };
}
