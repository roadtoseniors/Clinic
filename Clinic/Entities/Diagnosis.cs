using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Diagnosis
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Visit> Visits { get; set; } = new List<Visit>();
}
