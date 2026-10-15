namespace Clinic.API;
public static class RuntimeSchema
{
    public static async Task EnsureAsync(MyDbContext db,CancellationToken ct=default)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS revoked_session (id varchar(32) PRIMARY KEY, expires_at timestamp with time zone NOT NULL); CREATE TABLE IF NOT EXISTS auth_state (id integer PRIMARY KEY CHECK(id=1), epoch varchar(32) NOT NULL);",ct);
        var epoch=Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth_state(id,epoch) VALUES(1,{epoch}) ON CONFLICT(id) DO NOTHING",ct);
    }
    public static Task<string> EpochAsync(MyDbContext db,CancellationToken ct) => db.Database.SqlQueryRaw<string>("SELECT epoch AS \"Value\" FROM auth_state WHERE id=1").SingleAsync(ct);
    public static async Task ResetSessionsAsync(MyDbContext db,CancellationToken ct)
    {
        await EnsureAsync(db,ct);
        var epoch=Guid.NewGuid().ToString("N");await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE auth_state SET epoch={epoch} WHERE id=1",ct);
    }
}
