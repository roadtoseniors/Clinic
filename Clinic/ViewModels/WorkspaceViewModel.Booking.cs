using System;
using System.Collections.Generic;
using System.Linq;
using Clinic.Data;
using Clinic.Workspace;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    // Сначала выбирают врача и дату, затем свободное время приёма.
    private void ConfigureBooking(HttpClinicGateway gateway, Dictionary<string, string> values)
    {
        var doctorField = Fields.First(field => field.Spec.Key == "booking_doctor");
        var dateField = Fields.First(field => field.Spec.Key == "booking_date");
        var timeField = Fields.First(field => field.Spec.Key == "slot");
        var originalSlot = gateway.BookingSlots.FirstOrDefault(slot =>
            slot.Values["slot"] == values.GetValueOrDefault("slot"));
        var doctorId = originalSlot?.Values.GetValueOrDefault("employee_id") ?? values.GetValueOrDefault("employee_id");

        doctorField.Selected = doctorField.Choices.FirstOrDefault(choice => choice.Id == doctorId)
            ?? doctorField.Choices.FirstOrDefault();
        dateField.Text = originalSlot?.Values.GetValueOrDefault("work_date") ?? DateTime.Today.ToString("yyyy-MM-dd");

        void UpdateTimes()
        {
            var selectedTime = timeField.Value;
            var availableTimes = BookingTimes(gateway.BookingSlots, doctorField.Value, dateField.Text);
            Replace(timeField.Choices, availableTimes);

            // Если старое время недоступно у выбранного врача, выбор очищается.
            timeField.Selected = timeField.Choices.FirstOrDefault(choice => choice.Id == selectedTime);
        }

        doctorField.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorField.Selected))
            {
                UpdateTimes();
            }
        };
        dateField.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorField.Text))
            {
                UpdateTimes();
            }
        };

        UpdateTimes();
    }

    public static IEnumerable<Choice> BookingTimes(IEnumerable<Record> slots, string doctor, string date)
    {
        return slots
            .Where(slot => slot.Values.GetValueOrDefault("employee_id") == doctor
                && slot.Values.GetValueOrDefault("work_date") == date)
            .OrderBy(slot => slot.Values.GetValueOrDefault("start_time"))
            .Select(slot => new Choice(slot.Values["slot"], slot.Values["start_time"]));
    }
}
