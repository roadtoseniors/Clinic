using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class BackupLog
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public int AccountId { get; set; }

    public string Action { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public long? SizeBytes { get; set; }

    public string Status { get; set; } = null!;

    public string? Message { get; set; }

    public virtual Account Account { get; set; } = null!;
}
