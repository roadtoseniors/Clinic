using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class PatientPolicy
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public string Kind { get; set; } = null!;

    public string Number { get; set; } = null!;

    public int CompanyId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public bool IsActive { get; set; }

    public virtual InsuranceCompany Company { get; set; } = null!;

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual Patient Patient { get; set; } = null!;
}
