using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class PayMethod
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public bool IsInsurance { get; set; }

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
