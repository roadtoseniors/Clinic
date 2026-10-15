using System;
using System.Collections.Generic;
using System.Linq;
using Clinic.Data;
using Clinic.Workspace;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    // A person chooses a doctor, then a day, then one of that doctor's free times.
    private void ConfigureBooking(HttpClinicGateway gateway, Dictionary<string, string> values)
    {
        var doctor = Fields.First(f => f.Spec.Key == "booking_doctor");
        var date = Fields.First(f => f.Spec.Key == "booking_date");
        var time = Fields.First(f => f.Spec.Key == "slot");
        var originalSlot = gateway.BookingSlots.FirstOrDefault(r => r.Values["slot"] == values.GetValueOrDefault("slot"));
        var doctorId = originalSlot?.Values.GetValueOrDefault("employee_id") ?? values.GetValueOrDefault("employee_id");
        doctor.Selected = doctor.Choices.FirstOrDefault(c => c.Id == doctorId) ?? doctor.Choices.FirstOrDefault();
        date.Text = originalSlot?.Values.GetValueOrDefault("work_date") ?? DateTime.Today.ToString("yyyy-MM-dd");
        void UpdateTimes()
        {
            var selected = time.Value;
            var times = BookingTimes(gateway.BookingSlots, doctor.Value, date.Text);
            Replace(time.Choices, times);
            time.Selected = time.Choices.FirstOrDefault(c => c.Id == selected);
        }

        doctor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorField.Selected))
                UpdateTimes();
        };
        date.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorField.Text))
                UpdateTimes();
        };
        UpdateTimes();
    }

    public static IEnumerable<Choice> BookingTimes(IEnumerable<Record> slots, string doctor, string date) => slots.Where(r => r.Values.GetValueOrDefault("employee_id") == doctor && r.Values.GetValueOrDefault("work_date") == date).OrderBy(r => r.Values.GetValueOrDefault("start_time")).Select(r => new Choice(r.Values["slot"], r.Values["start_time"]));
}
