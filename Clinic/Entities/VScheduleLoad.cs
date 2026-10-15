using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class VScheduleLoad
{
    public int? ScheduleId { get; set; }

    public int? EmployeeId { get; set; }

    public DateOnly? WorkDate { get; set; }

    public int? SlotsTotal { get; set; }

    public int? SlotsBooked { get; set; }
}
