using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Invoice
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Total { get; set; }

    public string Status { get; set; } = null!;

    public string? Method { get; set; }

    public DateTime? PaidAt { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual ICollection<RenderedService> RenderedServices { get; set; } = new List<RenderedService>();
}
