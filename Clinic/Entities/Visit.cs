using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Visit
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public DateTime VisitDatetime { get; set; }

    public string? Complaints { get; set; }

    public string? Anamnesis { get; set; }

    public string? ObjectiveStatus { get; set; }

    public string? DiagnosisCode { get; set; }

    public string? Recommendations { get; set; }

    public bool IsClosed { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Diagnosis? DiagnosisCodeNavigation { get; set; }

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    public virtual ICollection<Referral> Referrals { get; set; } = new List<Referral>();

    public virtual ICollection<RenderedService> RenderedServices { get; set; } = new List<RenderedService>();
}
