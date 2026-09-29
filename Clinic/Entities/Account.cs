using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Account
{
    public int Id { get; set; }

    public string Login { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public int RoleId { get; set; }

    public int? EmployeeId { get; set; }

    public int? PatientId { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastLogin { get; set; }

    public virtual ICollection<Audit> Audits { get; set; } = new List<Audit>();

    public virtual Employee? Employee { get; set; }

    public virtual Patient? Patient { get; set; }

    public virtual Role Role { get; set; } = null!;
}
