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

    public char Gender { get; set; }

    public string? Snils { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public DateOnly CreatedDate { get; set; }

    public bool IsActive { get; set; }

    public virtual Account? Account { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<Audit> Audits { get; set; } = new List<Audit>();

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual PatientAttachment? PatientAttachment { get; set; }

    public virtual ICollection<PatientPolicy> PatientPolicies { get; set; } = new List<PatientPolicy>();
}
