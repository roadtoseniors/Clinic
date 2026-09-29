using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Referral
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public int ServiceId { get; set; }

    public string Status { get; set; } = null!;

    public int? ExecutorId { get; set; }

    public DateTime? DoneAt { get; set; }

    public string? Result { get; set; }

    public virtual Employee? Executor { get; set; }

    public virtual Service Service { get; set; } = null!;

    public virtual Visit Visit { get; set; } = null!;
}
