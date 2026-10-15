using System;
using System.Collections.Generic;
using System.Linq;

namespace Clinic.Workspace;

public static class Catalog
{
    public static string RoleSubtitle(string role) => role switch
    {
        "Пациент" => "Ваши записи, медицинская карта и результаты исследований",
        "Регистратор" => "Расписание, картотека пациентов и расчёты за услуги",
        "Врач" => "Ваши пациенты, приёмы и результаты исследований",
        _ => "Пользователи, справочники и контроль работы поликлиники"
    };
    public static string WorkspaceNote(string role) => role switch
    {
        "Пациент" => "Запишитесь к врачу, просмотрите рекомендации после приёма и готовые результаты исследований.",
        "Регистратор" => "Ведите картотеку, подбирайте свободное время приёма и оформляйте документы и оплату.",
        "Врач" => "Откройте приём из списка записей, заполните протокол и оформите назначения и направления.",
        _ => "Управляйте доступом сотрудников, обновляйте справочники и отслеживайте работу поликлиники."
    };
    public static Section[] Sections(string role)
    {
        var items = new List<Section> { new("home","Главная","⌂","Сводка на сегодня",[]) };
        if(role is "Пациент") items.AddRange([
            new("appointments","Мои записи","◷","Предстоящие приёмы и история записей",["Дата и время","Врач","Специальность","Кабинет"],"booking","Записаться"),
            new("slots","Запись к врачу","+","Выберите свободное время приёма",["Дата и время","Врач","Специальность","Кабинет"]),
            new("patients","Моя карта","♡","Личные данные и медицинская история",["ФИО","Дата рождения","Полис ОМС","Телефон"]),
            new("visits","Посещения","▤","Диагнозы и рекомендации врача",["Дата","Пациент","Врач","Диагноз"]),
            new("prescriptions","Назначения","✚","Лечение и рецепты",["Препарат","Пациент","Дозировка","Врач"]),
            new("referrals","Исследования","◉","Направления и готовые результаты",["Исследование","Пациент","Дата","Исполнитель"]),
            new("invoices","Мои счета","₽","Услуги и история оплаты",["Счёт","Пациент","Сумма","Оплачено"]),
            new("documents","Документы","▱","Согласия, договоры и справки",["Документ","Пациент","Дата","Автор"]),
            new("notifications","Уведомления","◌","Изменения записей и результаты",["Событие","Дата","Сообщение","" ])]);
        if(role is "Регистратор") items.AddRange([
            new("patients","Пациенты","♙","Картотека, полисы и прикрепление",["ФИО","Дата рождения","Полис ОМС","Телефон"],"patient","Добавить пациента"),
            new("schedule","Расписание","▦","Графики врачей и кабинеты",["Дата","Врач","Время","Кабинет"],"schedule","Добавить график"),
            new("appointments","Записи на приём","◷","Запись, перенос и отмена",["Дата и время","Пациент","Врач","Кабинет"],"booking","Записать пациента"),
            new("slots","Свободные слоты","+","Доступное время приёма",["Дата и время","Врач","Специальность","Кабинет"]),
            new("rendered","Платные услуги","₽","Фактически оказанные услуги",["Услуга","Пациент","Дата","Стоимость"],"rendered","Учесть услугу"),
            new("invoices","Счета и оплата","▤","Начисления, платежи и ДМС",["Счёт","Пациент","Сумма","Оплачено"]),
            new("documents","Документы","▱","Согласия и договоры",["Документ","Пациент","Дата","Автор"],"document","Оформить документ")]);
        if(role is "Врач") items.AddRange([
            new("appointments","Мои приёмы","◷","Пациенты, записанные к вам",["Дата и время","Пациент","Карта","Кабинет"]),
            new("schedule","Моё расписание","▦","Рабочие дни и время приёма",["Дата","Врач","Время","Кабинет"]),
            new("patients","Пациенты","♙","Карты ваших пациентов",["ФИО","Дата рождения","Полис ОМС","Телефон"]),
            new("visits","Протоколы","▤","Осмотры, диагнозы и рекомендации",["Дата","Пациент","Врач","Диагноз"],"visit"),
            new("prescriptions","Назначения","✚","Лекарства и рецепты",["Препарат","Пациент","Дозировка","Врач"],"prescription","Добавить назначение"),
            new("referrals","Исследования","◉","Направления и результаты",["Исследование","Пациент","Дата","Исполнитель"],"referral","Выдать направление"),
            new("documents","Справки","▱","Документы по приёмам",["Документ","Пациент","Дата","Автор"],"document","Оформить справку")]);
        if(role is "Администратор") items.AddRange([
            new("accounts","Пользователи","♙","Учётные записи и роли",["Логин","ФИО","Роль","Последний вход"],"account","Создать пользователя"),
            new("employees","Сотрудники","♙","Должности и специальности",["ФИО","Должность","Специальность","Телефон"],"employee","Добавить сотрудника"),
            new("services","Услуги и цены","₽","Прайс-лист поликлиники",["Услуга","Категория","Цена",""],"service","Добавить услугу"),
            new("dictionary","Справочники","▤","Специальности, кабинеты, МКБ-10 и лекарства",["Справочник","Код","Название",""],"dictionary","Добавить запись"),
            new("reports","Отчёты","▥","Посещаемость, загрузка, диагнозы и выручка",["Показатель","Значение","Период","" ]),
            new("audit","Журнал аудита","◉","Обращения к данным и изменения",["Дата","Пользователь","Действие","Объект"]),
            new("backup","Резервные копии","▱","Сохранение и восстановление данных поликлиники",[])]);
        return items.ToArray();
    }
    public static FieldSpec[] Fields(string kind) => kind switch
    {
        "patient" => [new("last_name","Фамилия",true),new("first_name","Имя",true),new("middle_name","Отчество"),new("birth_date","Дата рождения (ГГГГ-ММ-ДД)",true),new("gender","Пол",true,"gender"),new("oms_policy","Полис ОМС (16 цифр)",true),new("snils","СНИЛС"),new("dms_policy","Полис ДМС"),new("insurance_org","Страховая организация"),new("passport","Паспорт"),new("phone","Телефон"),new("address","Адрес"),new("district","Участок"),new("primary_doctor_id","Участковый врач",Lookup:"doctors"),new("blood_group","Группа крови"),new("allergies","Аллергии",Multiline:true)],
        "schedule" => [new("employee_id","Врач",true,"doctors"),new("room_id","Кабинет",true,"rooms"),new("work_date","Дата (ГГГГ-ММ-ДД)",true,Default:DateTime.Today.ToString("yyyy-MM-dd")),new("start_time","Начало (ЧЧ:ММ)",true,Default:"09:00"),new("end_time","Окончание (ЧЧ:ММ)",true,Default:"16:00"),new("slot_minutes","Длительность приёма, минут",true,Default:"30")],
        "booking" => [new("patient_id","Пациент",true,"patients"),new("slot","Свободное время",true,"slots")],
        "visit" => [new("complaints","Жалобы",true,Multiline:true),new("anamnesis","Анамнез",Multiline:true),new("objective_status","Объективный статус",Multiline:true),new("diagnosis_code","Диагноз МКБ-10",true,"diagnoses"),new("recommendations","Рекомендации",Multiline:true)],
        "prescription" => [new("visit_id","Приём",true,"open_visits"),new("drug_id","Препарат",true,"drugs"),new("dosage","Дозировка",true),new("instruction","Инструкция",true,Multiline:true),new("is_recipe","Рецептурный препарат",true,"boolean",Default:"false"),new("valid_until","Действует до (ГГГГ-ММ-ДД)")],
        "referral" => [new("visit_id","Приём",true,"open_visits"),new("service_id","Исследование / консультация",true,"services"),new("consultation_doctor_id","Врач для консультации",Lookup:"doctors")],
        "result" => [new("result","Заключение",true,Multiline:true),new("file_name","Файл результата")],
        "rendered" => [new("visit_id","Приём",true,"visits"),new("service_id","Услуга",true,"services"),new("quantity","Количество",true,Default:"1")],
        "payment" => [new("amount","Сумма платежа",true),new("method_id","Способ оплаты",true,"methods")],
        "document" => [new("patient_id","Пациент",true,"patients"),new("kind","Вид документа",true,"document_types"),new("content","Текст документа",true,Multiline:true)],
        "account" => [new("login","Логин",true),new("password","Новый пароль (пусто — сохранить текущий)",Secret:true),new("role_id","Роль",true,"roles"),new("employee_id","Сотрудник",Lookup:"employees"),new("patient_id","Пациент",Lookup:"patients")],
        "employee" => [new("last_name","Фамилия",true),new("first_name","Имя",true),new("middle_name","Отчество"),new("post_id","Должность",true,"posts"),new("specialty_id","Специальность",Lookup:"specialties"),new("phone","Телефон")],
        "service" => [new("name","Название",true),new("group_id","Категория",true,"groups"),new("price","Цена, ₽",true,Default:"0")],
        "dictionary" => [new("table","Справочник",true,"dictionary_types"),new("code","Код МКБ-10 / этаж кабинета"),new("name","Название / номер кабинета",true)],
        "registration" => [new("oms_policy","Полис ОМС (16 цифр)",true),new("birth_date","Дата рождения (ГГГГ-ММ-ДД)",true),new("login","Логин",true),new("password","Пароль (от 8 символов)",true,Secret:true)],
        "password" => [new("old_password","Текущий пароль",true,Secret:true),new("new_password","Новый пароль (от 8 символов)",true,Secret:true),new("confirmation","Подтверждение нового пароля",true,Secret:true)],
        _ => []
    };
}
