using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class RenderedService
{
    public int Id { get; set; }

    public int VisitId { get; set; }

    public int ServiceId { get; set; }

    public int? InvoiceId { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime RenderDate { get; set; }

    public virtual Invoice? Invoice { get; set; }

    public virtual Service Service { get; set; } = null!;

    public virtual Visit Visit { get; set; } = null!;
}
