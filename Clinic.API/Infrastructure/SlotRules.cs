namespace Clinic.API;

// Сетка слотов расписания: с StartTime до EndTime с шагом SlotMinutes
public static class SlotRules
{
    public static IEnumerable<TimeOnly> All(TimeOnly start, TimeOnly end, int slotMinutes)
    {
        if (slotMinutes <= 0) yield break;   // иначе цикл никогда не закончится
        var step = TimeSpan.FromMinutes(slotMinutes);
        for (var t = start.ToTimeSpan(); t + step <= end.ToTimeSpan(); t += step)
            yield return TimeOnly.FromTimeSpan(t);
    }

    public static bool IsOnGrid(TimeOnly start, TimeOnly end, int slotMinutes, TimeOnly time)
    {
        if (slotMinutes <= 0) return false;
        var slot = TimeSpan.FromMinutes(slotMinutes);
        var offset = time.ToTimeSpan() - start.ToTimeSpan();
        return offset >= TimeSpan.Zero
            && time.ToTimeSpan() + slot <= end.ToTimeSpan()
            && offset.Ticks % slot.Ticks == 0;
    }

    // Проверка слота перед записью или переносом.
    // База тоже проверяет сетку (триггер) и занятость (уникальный индекс), здесь даём понятные сообщения.
    public static async Task<IResult?> CheckAsync(MyDbContext db, int scheduleId, TimeOnly time, int? exceptAppointmentId, CancellationToken ct)
    {
        var schedule = await db.Schedules.AsNoTracking()
            .Where(s => s.Id == scheduleId)
            .Select(s => new { s.WorkDate, s.StartTime, s.EndTime, s.SlotMinutes, EmployeeActive = s.Employee.IsActive })
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
            return Err.NotFound("Расписание не найдено");
        if (!schedule.EmployeeActive)
            return Err.Conflict("Врач больше не ведёт приём");
        if (!IsOnGrid(schedule.StartTime, schedule.EndTime, schedule.SlotMinutes, time))
            return Err.BadRequest("Такого времени нет в расписании врача");
        if (schedule.WorkDate.ToDateTime(time) < Clock.Now)
            return Err.BadRequest("Нельзя записаться на прошедшее время");

        var taken = await db.Appointments.AnyAsync(a =>
            a.ScheduleId == scheduleId
            && a.StartTime == time
            && a.Status != AppointmentStatus.Cancelled
            && (exceptAppointmentId == null || a.Id != exceptAppointmentId), ct);

        return taken ? Err.Conflict("Это время уже занято") : null;
    }
}
