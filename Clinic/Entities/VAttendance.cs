using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class VAttendance
{
    public DateOnly? Day { get; set; }

    public int? Planned { get; set; }

    public int? Completed { get; set; }

    public int? NoShow { get; set; }

    public int? Cancelled { get; set; }
}
