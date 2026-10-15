using System;
using System.Collections.Generic;

namespace Clinic.Entities;

public partial class ReferralFile
{
    public int Id { get; set; }

    public int ReferralId { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public byte[] Data { get; set; } = null!;

    public int? SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; }

    public int? UploadedBy { get; set; }

    public virtual Referral Referral { get; set; } = null!;

    public virtual Employee? UploadedByNavigation { get; set; }
}
