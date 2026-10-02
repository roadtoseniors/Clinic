using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class District
{
    public int Id { get; set; }

    public string Number { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<PatientAttachment> PatientAttachments { get; set; } = new List<PatientAttachment>();
}
