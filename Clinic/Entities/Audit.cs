using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Audit
{
    public long Id { get; set; }

    public int AccountId { get; set; }

    public int? PatientId { get; set; }

    public DateTime ActedAt { get; set; }

    public string Action { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public int? RecordId { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Patient? Patient { get; set; }
}
