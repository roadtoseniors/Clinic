using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Clinic.Data;

public sealed partial class ClinicDatabase
{
    private static readonly string[] BackupTables=["specialty","post","room","service_group","diagnosis","drug","service","employee","patient","account","user_theme","schedule","appointment","visit","invoice","visit_service","referral","prescription","patient_document","notification","payment","audit"];
    public sealed record DatabaseBackup(int Version,string Database,DateTime CreatedAt,Dictionary<string,List<Dictionary<string,string?>>> Tables);
    public async Task<string> BackupAsync(ClinicSession session)
    {
        await using var c=await OpenAsync();await CheckAsync(c,session,"Администратор");
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var data=new Dictionary<string,List<Dictionary<string,string?>>>();
        foreach(var table in BackupTables)
        {
            var rows=new List<Dictionary<string,string?>>();
            await using var cmd=Command(c,$"SELECT * FROM {table}");await using var reader=await cmd.ExecuteReaderAsync();
            while(await reader.ReadAsync())
            {
                var row=new Dictionary<string,string?>();
                for(int i=0;i<reader.FieldCount;i++)
                {
                    var value=reader.GetValue(i);
                    row[reader.GetName(i)]=value switch { DBNull=>null,byte[] bytes=>Convert.ToBase64String(bytes),DateTime dt=>dt.ToString("O",CultureInfo.InvariantCulture),_=>Format(value) };
                }
                rows.Add(row);
            }
            data[table]=rows;
        }
        await tx.CommitAsync();return JsonSerializer.Serialize(new DatabaseBackup(1,"clinic_course_test",DateTime.UtcNow,data),new JsonSerializerOptions { WriteIndented=true });
    }
    public async Task RestoreAsync(ClinicSession session,string json)
    {
        var backup=JsonSerializer.Deserialize<DatabaseBackup>(json)??throw new InvalidOperationException("Некорректная резервная копия.");
        if(backup.Version!=1||backup.Database!="clinic_course_test"||backup.Tables.Keys.Except(BackupTables).Any())throw new InvalidOperationException("Формат резервной копии не поддерживается.");
        await using var c=await OpenAsync();await CheckAsync(c,session,"Администратор");
        if(await Scalar(c,"SELECT current_database()") is not string name||name!="clinic_course_test")throw new InvalidOperationException("Восстановление разрешено только в clinic_course_test.");
        await using var tx=await c.BeginTransactionAsync();
        foreach(var table in BackupTables)
        {
            if(!backup.Tables.TryGetValue(table,out var rows))throw new InvalidOperationException("В копии отсутствует таблица "+table);
            var types=new Dictionary<string,string>();
            await using(var cmd=Command(c,"SELECT column_name,udt_name FROM information_schema.columns WHERE table_schema='public' AND table_name=@t ORDER BY ordinal_position",("t",table)))
            await using(var reader=await cmd.ExecuteReaderAsync()) while(await reader.ReadAsync())types[reader.GetString(0)]=reader.GetString(1);
            var key=table=="diagnosis"?"code":table=="user_theme"?"account_id":"id";
            foreach(var row in rows)
            {
                if(!row.ContainsKey(key)||row.Keys.Except(types.Keys).Any())throw new InvalidOperationException("Структура резервной копии отличается от текущей БД.");
                var columns=row.Keys.ToArray();
                var immutable=table is "payment" or "audit" or "patient_document";
                var updates=columns.Where(k=>k!=key).ToArray();
                var conflict=immutable||updates.Length==0?"DO NOTHING":"DO UPDATE SET "+string.Join(',',updates.Select(k=>$"{k}=EXCLUDED.{k}"));
                await using var command=Command(c,$"INSERT INTO {table} ({string.Join(',',columns)}) VALUES ({string.Join(',',columns.Select(k=>'@'+k))}) ON CONFLICT ({key}) {conflict}");
                foreach(var (column,text) in row)
                {
                    var type=types[column];
                    NpgsqlDbType dbType=type switch { "int4"=>NpgsqlDbType.Integer,"int8"=>NpgsqlDbType.Bigint,"int2"=>NpgsqlDbType.Smallint,"numeric"=>NpgsqlDbType.Numeric,"bool"=>NpgsqlDbType.Boolean,"date"=>NpgsqlDbType.Date,"time"=>NpgsqlDbType.Time,"timestamp"=>NpgsqlDbType.Timestamp,"timestamptz"=>NpgsqlDbType.TimestampTz,"bytea"=>NpgsqlDbType.Bytea,_=>NpgsqlDbType.Text };
                    object value=text is null?DBNull.Value:type switch
                    {
                        "int4"=>int.Parse(text,CultureInfo.InvariantCulture),"int8"=>long.Parse(text,CultureInfo.InvariantCulture),"int2"=>short.Parse(text,CultureInfo.InvariantCulture),
                        "numeric"=>decimal.Parse(text,CultureInfo.InvariantCulture),"bool"=>bool.Parse(text),"date"=>DateTime.Parse(text,CultureInfo.InvariantCulture),
                        "time"=>TimeSpan.Parse(text,CultureInfo.InvariantCulture),"timestamp"=>DateTime.SpecifyKind(DateTime.Parse(text,CultureInfo.InvariantCulture),DateTimeKind.Unspecified),
                        "timestamptz"=>DateTime.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime(),"bytea"=>Convert.FromBase64String(text),_=>text
                    };
                    command.Parameters.AddWithValue(column,dbType,value);
                }
                await command.ExecuteNonQueryAsync();
            }
            if(key=="id"&&rows.Count>0)
            {
                var maximum=rows.Max(r=>long.Parse(r["id"]!,CultureInfo.InvariantCulture));
                // Only advance identities. No setval privileges and no sequence rollback.
                long next;do { next=Convert.ToInt64(await Scalar(c,$"SELECT nextval(pg_get_serial_sequence('{table}','id'))")); }while(next<maximum);
            }
        }
        await Audit(c,session,"UPDATE","backup");await tx.CommitAsync();
    }
}
