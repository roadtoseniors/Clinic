using Clinic.API;
using Clinic.Context;
using Clinic.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

public static class ApiFixture
{
    public static async Task PrepareTodayAsync(string? fixtureConnection=null)
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Clinic.API/api.local.json"));
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(path));var cs=fixtureConnection??settings.RootElement.GetProperty("ConnectionStrings").GetProperty("Clinic").GetString()!;
        if(new NpgsqlConnectionStringBuilder(cs).Database is not "clinic_api_integration_test" && !(fixtureConnection is not null && new NpgsqlConnectionStringBuilder(cs).Database!.StartsWith("clinic_functional_",StringComparison.Ordinal)))throw new InvalidOperationException("Fixture preparation requires the isolated API test database.");
        await using var db=new MyDbContext(new DbContextOptionsBuilder<MyDbContext>().UseNpgsql(cs).Options);
        var doctor=await db.Accounts.Where(a=>a.Login=="sokolov").Select(a=>a.EmployeeId!.Value).SingleAsync();
        var patient=await db.Accounts.Where(a=>a.Login=="kirillov").Select(a=>a.PatientId!.Value).SingleAsync();
        var day=DateOnly.FromDateTime(DateTime.Today);var schedule=await db.Schedules.SingleAsync(s=>s.EmployeeId==doctor&&s.WorkDate==day);
        if(await db.Appointments.AnyAsync(a=>a.ScheduleId==schedule.Id&&a.Status=="запланирована"&&a.Visit==null))return;
        var occupied=await db.Appointments.Where(a=>a.ScheduleId==schedule.Id&&a.Status!="отменена").Select(a=>a.StartTime).ToListAsync();
        var free=SlotRules.All(schedule.StartTime,schedule.EndTime,schedule.SlotMinutes).First(t=>!occupied.Contains(t));
        db.Appointments.Add(new(){ScheduleId=schedule.Id,PatientId=patient,StartTime=free,Status="запланирована",Source="регистратура",CreatedAt=DateTime.Now});await db.SaveChangesAsync();
    }
    public static async Task SeedAsync()
    {
        var cs=Environment.GetEnvironmentVariable("CLINIC_API_TEST_DB")??$"Host=127.0.0.1;Username={Environment.UserName};Database=clinic_api_integration_test";
        var config=new NpgsqlConnectionStringBuilder(cs);
        if(config.Database!="clinic_api_integration_test")throw new InvalidOperationException("Seed разрешён только в clinic_api_integration_test.");
        var owner=new NpgsqlConnectionStringBuilder(cs){Database="postgres"};
        await using(var c=new NpgsqlConnection(owner.ConnectionString))
        {
            await c.OpenAsync();await using var check=new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname='clinic_api_integration_test'",c);
            if(await check.ExecuteScalarAsync() is null){await using var create=new NpgsqlCommand("CREATE DATABASE clinic_api_integration_test",c);await create.ExecuteNonQueryAsync();}
        }
        await using var db=new MyDbContext(new DbContextOptionsBuilder<MyDbContext>().UseNpgsql(cs).Options);
        await using(var c=new NpgsqlConnection(cs))
        {
            await c.OpenAsync();await using var check=new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'",c);
            if(Convert.ToInt64(await check.ExecuteScalarAsync())>0)throw new InvalidOperationException("Тестовая база уже заполнена. Данные сохранены; повторная инициализация отменена.");

        }
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript().Replace("CREATE SEQUENCE ","CREATE SEQUENCE IF NOT EXISTS "));
        var permissions=typeof(Perm).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.IsLiteral&&f.Name!="ClaimType").Select(f=>new Permission{Code=(string)f.GetRawConstantValue()!,Name=f.Name}).ToList();
        var patient=new Role{Code="patient",Name="Пациент"};var registrar=new Role{Code="registrar",Name="Регистратор"};var doctor=new Role{Code="doctor",Name="Врач"};var admin=new Role{Code="admin",Name="Администратор"};
        void Allow(Role role,params string[] codes){foreach(var p in permissions.Where(p=>codes.Contains(p.Code)))role.PermissionCodes.Add(p);}
        Allow(patient,Perm.OwnView,Perm.OwnBook);
        Allow(registrar,Perm.PatientView,Perm.PatientManage,Perm.DocumentManage,Perm.ScheduleManage,Perm.AppointmentManage,Perm.ServiceRender,Perm.InvoiceManage);
        Allow(doctor,Perm.PatientView,Perm.DoctorSchedule,Perm.MedcardView,Perm.VisitManage,Perm.ReferralIssue,Perm.ReferralView,Perm.ReferralResult);
        foreach(var p in permissions)admin.PermissionCodes.Add(p);
        var specialty=new Specialty{Name="Терапия"};var postDoctor=new Post{Name="Врач"};var postRegistrar=new Post{Name="Регистратор"};var postAdmin=new Post{Name="Администратор"};
        var company=new InsuranceCompany{Name="МедГарант",IsActive=true};var group=new ServiceGroup{Name="Приём и диагностика"};var room=new Room{Number="101",Floor=1};var district=new District{Number="1",Name="Участок №1"};
        var dr=new Employee{LastName="Соколов",FirstName="Андрей",Post=postDoctor,Specialty=specialty,IsActive=true};
        var reg=new Employee{LastName="Петрова",FirstName="Анна",Post=postRegistrar,IsActive=true};var manager=new Employee{LastName="Орлов",FirstName="Сергей",Post=postAdmin,IsActive=true};
        var p1=new Patient{LastName="Кириллов",FirstName="Максим",BirthDate=new(1985,3,14),Gender='М',IsActive=true};var p2=new Patient{LastName="Захарова",FirstName="Ирина",BirthDate=new(1992,7,21),Gender='Ж',IsActive=true};
        var hash=Passwords.Hash("demo12345");
        db.AddRange(patient,registrar,doctor,admin,company,district,room);
        db.AddRange(new Account{Login="kirillov",PasswordHash=hash,Role=patient,Patient=p1,IsActive=true,UserTheme="light"},new Account{Login="petrova",PasswordHash=hash,Role=registrar,Employee=reg,IsActive=true,UserTheme="light"},new Account{Login="sokolov",PasswordHash=hash,Role=doctor,Employee=dr,IsActive=true,UserTheme="light"},new Account{Login="admin",PasswordHash=hash,Role=admin,Employee=manager,IsActive=true,UserTheme="light"});
        db.AddRange(new PatientPolicy{Patient=p1,Kind="ОМС",Number="0000000000000001",Company=company,ValidFrom=DateOnly.FromDateTime(DateTime.Today),IsActive=true},new PatientPolicy{Patient=p2,Kind="ОМС",Number="0000000000000002",Company=company,ValidFrom=DateOnly.FromDateTime(DateTime.Today),IsActive=true},new PatientPolicy{Patient=p1,Kind="ДМС",Number="ДМС-000001",Company=company,ValidFrom=DateOnly.FromDateTime(DateTime.Today),IsActive=true});
        db.AddRange(new PayMethod{Name="Наличные"},new PayMethod{Name="Банковская карта"},new PayMethod{Name="ДМС",IsInsurance=true});
        db.AddRange(new DocumentType{Name="Согласие на обработку данных"},new DocumentType{Name="Договор платных услуг"},new DocumentType{Name="Справка"},new Drug{Name="Парацетамол"},new Diagnosis{Code="J06.9",Name="ОРВИ"});
        db.AddRange(new Service{Name="Приём терапевта",Group=group,Price=1800,IsActive=true},new Service{Name="Общий анализ крови",Group=group,Price=450,IsActive=true});
        var today=new Schedule{Employee=dr,Room=room,WorkDate=DateOnly.FromDateTime(DateTime.Today),StartTime=new(9,0),EndTime=new(17,0),SlotMinutes=30};
        db.Add(today);db.Add(new Appointment{Schedule=today,Patient=p1,StartTime=new(9,0),Status="запланирована",Source="регистратура",CreatedAt=DateTime.Now});
        for(var i=1;i<=14;i++)db.Add(new Schedule{Employee=dr,Room=room,WorkDate=DateOnly.FromDateTime(DateTime.Today.AddDays(i)),StartTime=new(9,0),EndTime=new(17,0),SlotMinutes=30});
        db.Add(new Notification{Patient=p1,CreatedAt=DateTime.Now,Message="Добро пожаловать в личный кабинет поликлиники",IsRead=false});
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notify_appointment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN INSERT INTO notification(patient_id,created_at,message,is_read) VALUES(NEW.patient_id,now(),'Запись №'||NEW.id||': '||NEW.status,false); RETURN NEW; END $$;
            CREATE TRIGGER notify_appointment AFTER INSERT OR UPDATE ON appointment FOR EACH ROW EXECUTE FUNCTION notify_appointment();
            """);
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        var path=Path.Combine(root,"Clinic.API","api.local.json");
        if(!Directory.Exists(Path.GetDirectoryName(path)))throw new InvalidOperationException("Не найден каталог Clinic.API для локальной настройки.");
        if(File.Exists(path))throw new InvalidOperationException("api.local.json уже существует. База создана; существующая настройка сохранена.");
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{ConnectionStrings=new{Clinic=cs},Auth=new{SigningKey=Convert.ToHexString(RandomNumberGenerator.GetBytes(32))},Backup=new{PgDumpPath=File.Exists("/opt/homebrew/bin/pg_dump")?"/opt/homebrew/bin/pg_dump":"pg_dump",PgRestorePath=File.Exists("/opt/homebrew/bin/pg_restore")?"/opt/homebrew/bin/pg_restore":"pg_restore"}},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("API test database seeded. Accounts: kirillov / petrova / sokolov / admin; password: demo12345.");
    }
}
