using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Permission
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
