using System;

namespace Clinic.Data;

public sealed record CaptchaChallenge(string ChallengeId, string ImageBase64, DateTimeOffset ExpiresAt);
public sealed class CaptchaRequiredException(string message, CaptchaChallenge challenge) : InvalidOperationException(message)
{
    public CaptchaChallenge Challenge { get; } = challenge;
}

public sealed class SessionExpiredException(string message) : InvalidOperationException(message);
