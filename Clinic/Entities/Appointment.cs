using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Appointment
{
    public int Id { get; set; }

    public int ScheduleId { get; set; }

    public int PatientId { get; set; }

    public TimeOnly StartTime { get; set; }

    public string Status { get; set; } = null!;

    public string Source { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual Schedule Schedule { get; set; } = null!;

    public virtual Visit? Visit { get; set; }
}
