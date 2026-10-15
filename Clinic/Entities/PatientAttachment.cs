using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class PatientAttachment
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public int DistrictId { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly AttachedFrom { get; set; }

    public DateOnly? AttachedTo { get; set; }

    public virtual District District { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual Patient Patient { get; set; } = null!;
}
