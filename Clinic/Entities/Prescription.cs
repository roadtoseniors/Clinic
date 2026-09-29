using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Prescription
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public int DrugId { get; set; }

    public string? Dosage { get; set; }

    public string? Instruction { get; set; }

    public bool IsRecipe { get; set; }

    public virtual Drug Drug { get; set; } = null!;

    public virtual Visit Visit { get; set; } = null!;
}
