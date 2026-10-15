using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Buffers.Binary;

namespace Clinic.API;

public sealed record CaptchaDto(string ChallengeId, string ImageBase64, DateTimeOffset ExpiresAt);
public sealed record CaptchaRequest(string Login);

public sealed class LoginProtection(TimeProvider time, Func<string>? codeFactory = null)
{
    private sealed class Attempts
    {
        public int Failures;
        public DateTimeOffset LastSeen;
        public string? Id;
        public string? Answer;
        public CaptchaDto? Challenge;
    }
    private readonly ConcurrentDictionary<string, Attempts> _attempts = new();
    public static string Key(string login, string? address) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(login.Trim().ToUpperInvariant() + "|" + address)));
    private Attempts Get(string key)
    {
        var now = time.GetUtcNow();
        if (_attempts.Count > 4096)
            foreach (var pair in _attempts.Where(p => now - p.Value.LastSeen > TimeSpan.FromMinutes(15)).Take(1024))
                _attempts.TryRemove(pair.Key, out _);
        var state = _attempts.GetOrAdd(key, _ => new Attempts { LastSeen = now });
        lock (state)
        {
            if (now - state.LastSeen > TimeSpan.FromMinutes(15))
            { state.Failures = 0; state.Id = state.Answer = null; state.Challenge = null; }
            state.LastSeen = now;
        }
        return state;
    }
    public bool Accepts(string key, string? id, string? answer)
    {
        var state = Get(key);
        lock (state)
        {
            if (state.Failures < 3) return true;
            var valid = state.Id == id && state.Answer is not null && state.Challenge!.ExpiresAt > time.GetUtcNow()
                && string.Equals(state.Answer, answer?.Trim(), StringComparison.OrdinalIgnoreCase);
            state.Id = state.Answer = null; state.Challenge = null;
            return valid;
        }
    }
    public CaptchaDto? Failed(string key)
    {
        var state = Get(key);
        lock (state) { state.Failures++; return state.Failures >= 3 ? Issue(state) : null; }
    }
    public CaptchaDto? Refresh(string key)
    {
        var state = Get(key);
        lock (state) return state.Failures >= 3 ? Issue(state) : null;
    }
    public void Reset(string key) => _attempts.TryRemove(key, out _);
    private CaptchaDto Issue(Attempts state)
    {
        const string alphabet = "23456789ACDEFHJKLMNPRTXY";
        var code = codeFactory?.Invoke() ?? new string(Enumerable.Range(0, 5).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        state.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); state.Answer = code;
        return state.Challenge = new(state.Id, Convert.ToBase64String(CaptchaPng.Render(code)), time.GetUtcNow().AddMinutes(3));
    }
}

internal static class CaptchaPng
{
    private static readonly string[] Patterns = [
        "11110/00001/00001/01110/10000/10000/11111", "11110/00001/00001/01110/00001/00001/11110",
        "00010/00110/01010/10010/11111/00010/00010", "11111/10000/10000/11110/00001/00001/11110",
        "01110/10000/10000/11110/10001/10001/01110", "11111/00001/00010/00100/01000/01000/01000",
        "01110/10001/10001/01110/10001/10001/01110", "01110/10001/10001/01111/00001/00001/01110",
        "01110/10001/10001/11111/10001/10001/10001", "01111/10000/10000/10000/10000/10000/01111",
        "11110/10001/10001/10001/10001/10001/11110", "11111/10000/10000/11110/10000/10000/11111",
        "11111/10000/10000/11110/10000/10000/10000", "10001/10001/10001/11111/10001/10001/10001",
        "00111/00010/00010/00010/00010/10010/01100", "10001/10010/10100/11000/10100/10010/10001",
        "10000/10000/10000/10000/10000/10000/11111", "10001/11011/10101/10101/10001/10001/10001",
        "10001/11001/11001/10101/10011/10011/10001", "11110/10001/10001/11110/10000/10000/10000",
        "11110/10001/10001/11110/10100/10010/10001", "11111/00100/00100/00100/00100/00100/00100",
        "10001/10001/01010/00100/01010/10001/10001", "10001/10001/01010/00100/00100/00100/00100"
    ];
    public static byte[] Render(string text)
    {
        const int width = 240, height = 80;
        var pixels = Enumerable.Repeat((byte)246, width * height * 3).ToArray();
        void Pixel(int x, int y, byte r, byte g, byte b)
        { if (x < 0 || y < 0 || x >= width || y >= height) return; var i = (y * width + x) * 3; pixels[i] = r; pixels[i+1] = g; pixels[i+2] = b; }
        for (var i = 0; i < 1100; i++) Pixel(RandomNumberGenerator.GetInt32(width), RandomNumberGenerator.GetInt32(height), 180, 204, 218);
        const string alphabet = "23456789ACDEFHJKLMNPRTXY";
        for (var c = 0; c < text.Length; c++)
        {
            var glyph = Patterns[alphabet.IndexOf(text[c])].Split('/');
            var top = RandomNumberGenerator.GetInt32(13, 27); var left = 18 + c * 43;
            for (var y = 0; y < 7; y++) for (var x = 0; x < 5; x++) if (glyph[y][x] == '1')
                for (var dy = 0; dy < 5; dy++) for (var dx = 0; dx < 5; dx++)
                    Pixel(left + x * 5 + dx + y / 3, top + y * 5 + dy, 19, 73, 102);
        }
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++) { raw.WriteByte(0); raw.Write(pixels, y * width * 3, width * 3); }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(raw.ToArray());
        using var png = new MemoryStream(); png.Write([137,80,78,71,13,10,26,10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR", header); Chunk(png, "IDAT", compressed.ToArray()); Chunk(png, "IEND", []); return png.ToArray();
    }
    private static void Chunk(Stream stream, string name, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
        var type = Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(data);
        uint crc = 0xffffffff;
        foreach (var b in type.Concat(data)) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
}
