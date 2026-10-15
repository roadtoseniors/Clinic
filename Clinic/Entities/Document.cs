using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Document
{
    public int Id { get; set; }

    public int TypeId { get; set; }

    public int PatientId { get; set; }

    public int? VisitId { get; set; }

    public string Number { get; set; } = null!;

    public DateTime IssuedAt { get; set; }

    public int IssuedBy { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? Content { get; set; }

    public virtual Employee IssuedByNavigation { get; set; } = null!;

    public virtual Patient Patient { get; set; } = null!;

    public virtual DocumentType Type { get; set; } = null!;

    public virtual Visit? Visit { get; set; }
}
