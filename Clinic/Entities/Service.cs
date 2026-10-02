using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Service
{
    public int Id { get; set; }

    public int GroupId { get; set; }

    public string Name { get; set; } = null!;

    public decimal Price { get; set; }

    public bool IsActive { get; set; }

    public virtual ServiceGroup Group { get; set; } = null!;

    public virtual ICollection<Referral> Referrals { get; set; } = new List<Referral>();

    public virtual ICollection<VisitService> VisitServices { get; set; } = new List<VisitService>();
}
