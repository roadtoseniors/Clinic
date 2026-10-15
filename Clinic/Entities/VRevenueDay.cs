using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class VRevenueDay
{
    public DateOnly? Day { get; set; }

    public string? Method { get; set; }

    public int? Invoices { get; set; }

    public decimal? Total { get; set; }
}
