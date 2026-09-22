using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class Patient
{
    public int Id { get; set; }

    public string CardNumber { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string FirstName { get; set; } = null!;

    public string? MiddleName { get; set; }

    public DateOnly BirthDate { get; set; }

    public string Gender { get; set; } = null!;

    public string? Snils { get; set; }

    public string OmsPolicy { get; set; } = null!;

    public string? DmsPolicy { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public DateOnly CreatedDate { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<UserAccount> UserAccounts { get; set; } = new List<UserAccount>();
}
