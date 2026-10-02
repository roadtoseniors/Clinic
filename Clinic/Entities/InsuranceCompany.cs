using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class InsuranceCompany
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<PatientPolicy> PatientPolicies { get; set; } = new List<PatientPolicy>();
}
