using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class AuditLog
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public int? PatientId { get; set; }

    public DateTime ActionDatetime { get; set; }

    public string ActionType { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public int? RecordId { get; set; }

    public virtual Patient? Patient { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}
