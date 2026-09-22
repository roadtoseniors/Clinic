using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Prescription
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public string Name { get; set; } = null!;

    public string? Dosage { get; set; }

    public string? Instruction { get; set; }

    public bool IsRecipe { get; set; }

    public DateOnly IssueDate { get; set; }

    public virtual Visit Visit { get; set; } = null!;
}
