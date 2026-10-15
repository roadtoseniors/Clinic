using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Role
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();

    public virtual ICollection<Permission> PermissionCodes { get; set; } = new List<Permission>();
}
