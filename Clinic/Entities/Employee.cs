using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Employee
{
    public int Id { get; set; }

    public string LastName { get; set; } = null!;

    public string FirstName { get; set; } = null!;

    public string? MiddleName { get; set; }

    public int PostId { get; set; }

    public int? SpecialtyId { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public virtual Account? Account { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<PatientAttachment> PatientAttachments { get; set; } = new List<PatientAttachment>();

    public virtual Post Post { get; set; } = null!;

    public virtual ICollection<ReferralFile> ReferralFiles { get; set; } = new List<ReferralFile>();

    public virtual ICollection<Referral> Referrals { get; set; } = new List<Referral>();

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    public virtual Specialty? Specialty { get; set; }
}
