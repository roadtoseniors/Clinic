using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class VMorbidity
{
    public DateOnly? Month { get; set; }

    public string? DiagnosisCode { get; set; }

    public string? DiagnosisName { get; set; }

    public int? Visits { get; set; }
}
