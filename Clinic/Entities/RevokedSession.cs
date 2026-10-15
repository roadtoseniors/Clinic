using System;
namespace Clinic.Entities;
public sealed class RevokedSession
{
    public string Id { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
