using Clinic.API;
internal static class LoginProtectionTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        var clock = new TestClock();
        var protection = new LoginProtection(clock, () => "AC234");
        var key = LoginProtection.Key("demo", "127.0.0.1");
        Check(protection.Accepts(key, null, null), "First attempt needs no captcha");
        Check(protection.Failed(key) is null, "First failure");
        Check(protection.Failed(key) is null, "Second failure");
        var third = protection.Failed(key)!;
        Check(third is not null, "Third failure shows captcha");
        Check(Convert.FromBase64String(third!.ImageBase64).Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }), "PNG image");
        Check(!protection.Accepts(key, null, null), "Cannot bypass captcha with missing answer");
        var refreshed = protection.Refresh(key)!;
        Check(refreshed.ChallengeId != third.ChallengeId, "Refresh replaces challenge");
        Check(!protection.Accepts(key, third.ChallengeId, "AC234"), "Old challenge rejected");
        refreshed = protection.Refresh(key)!;
        Check(protection.Accepts(key, refreshed.ChallengeId, "ac234"), "Case insensitive correct answer");
        Check(!protection.Accepts(key, refreshed.ChallengeId, "AC234"), "Single use answer");
        refreshed = protection.Refresh(key)!;
        clock.Now = clock.Now.AddMinutes(4);
        Check(!protection.Accepts(key, refreshed.ChallengeId, "AC234"), "Expired answer rejected");
        protection.Reset(key);
        Check(protection.Accepts(key, null, null), "Successful login resets attempts");
        return checks;
    }
}
