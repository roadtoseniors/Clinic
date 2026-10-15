using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Room
{
    public int Id { get; set; }

    public string Number { get; set; } = null!;

    public short Floor { get; set; }

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
}
