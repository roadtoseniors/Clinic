using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class ServiceGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}
