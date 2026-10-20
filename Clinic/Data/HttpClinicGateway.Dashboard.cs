using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway
{
    public async Task<LiveDashboard> DashboardAsync(ClinicSession session)
    {
        Check(session);
        var appointments = await DashboardRowsAsync(session, "appointments");
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var todayAppointments = appointments
            .Where(appointment => appointment.Values.GetValueOrDefault("work_date") == today)
            .ToList();

        DashboardMetric Metric(int count, string label, string detail)
        {
            return new DashboardMetric(count.ToString(), label, detail, "");
        }

        DashboardMetric[] metrics;
        List<Record> events;
        string eventTitle;
        if (session.Role == "Администратор")
        {
            var employees = await DashboardRowsAsync(session, "employees");
            var patients = await DashboardRowsAsync(session, "patients");
            metrics =
            [
                Metric(employees.Count(employee => employee.Status != "архив"), "Сотрудники", "В команде поликлиники"),
                Metric(patients.Count(patient => patient.Status != "архив"), "Пациенты", "Активные медицинские карты"),
                Metric(todayAppointments.Count, "Приёмы сегодня", "По расписанию врачей")
            ];
            events = employees.Take(3).ToList();
            eventTitle = "Команда поликлиники";
        }
        else if (session.Role == "Пациент")
        {
            var visits = await DashboardRowsAsync(session, "visits");
            var referrals = await DashboardRowsAsync(session, "referrals");
            metrics =
            [
                Metric(appointments.Count(appointment => appointment.Status == "запланирована"), "Предстоящие приёмы", "Ваши записи к врачам"),
                Metric(visits.Count, "Посещения", "В медицинской карте"),
                Metric(referrals.Count(referral => referral.Status == "выполнено"), "Готовые результаты", "Можно посмотреть заключение")
            ];
            events = appointments.Where(appointment => appointment.Status == "запланирована").Take(3).ToList();
            eventTitle = "Ближайшие приёмы";
        }
        else
        {
            var patients = await DashboardRowsAsync(session, "patients");
            metrics =
            [
                Metric(todayAppointments.Count, "Приёмы сегодня", "По вашему расписанию"),
                Metric(todayAppointments.Count(appointment => appointment.Status == "запланирована"), "Ожидают приёма", "Записи на сегодня"),
                Metric(patients.Count(patient => patient.Status != "архив"), "Пациенты", "Доступные медицинские карты")
            ];
            events = todayAppointments.Take(3).ToList();
            eventTitle = "Сегодня на приёме";
        }

        var items = new List<DashboardItem>();
        foreach (var row in events)
        {
            if (session.Role == "Администратор")
            {
                var details = new[] { row.Cell2, row.Cell3 }.Where(value => value.Length > 0);
                items.Add(new DashboardItem("", row.Cell1, string.Join(" · ", details), row.Status));
            }
            else
            {
                items.Add(new DashboardItem(row.Cell1, row.Cell2, row.Cell3, row.Status));
            }
        }

        return new LiveDashboard(metrics, items.ToArray(), eventTitle, Catalog.WorkspaceNote(session.Role));
    }

    private Task<List<Record>> DashboardRowsAsync(ClinicSession session, string page)
    {
        if (AccessPresentation.Read(session.Role, page, Permissions))
        {
            return ListAsync(session, page);
        }

        return Task.FromResult(new List<Record>());
    }
}
