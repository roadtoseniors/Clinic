using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Referral
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public int ServiceId { get; set; }

    public DateOnly IssueDate { get; set; }

    public string Status { get; set; } = null!;

    public int? ExecutorId { get; set; }

    public DateTime? ExecDate { get; set; }

    public string? ResultText { get; set; }

    public virtual Employee? Executor { get; set; }

    public virtual Service Service { get; set; } = null!;

    public virtual Visit Visit { get; set; } = null!;
}
