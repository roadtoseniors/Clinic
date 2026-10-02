using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class VInvoice
{
    public int? Id { get; set; }

    public int? PatientId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? Status { get; set; }

    public int? MethodId { get; set; }

    public int? PolicyId { get; set; }

    public DateTime? PaidAt { get; set; }

    public decimal? Total { get; set; }
}
