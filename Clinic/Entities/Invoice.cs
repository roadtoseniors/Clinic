using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Invoice
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Status { get; set; } = null!;

    public int? MethodId { get; set; }

    public DateTime? PaidAt { get; set; }

    public virtual PayMethod? Method { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual ICollection<VisitService> VisitServices { get; set; } = new List<VisitService>();
}
