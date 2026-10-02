using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Notification
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public virtual Patient Patient { get; set; } = null!;
}
