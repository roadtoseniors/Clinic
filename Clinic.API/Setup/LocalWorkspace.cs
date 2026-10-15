using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clinic.Workspace;
using Npgsql;

namespace Clinic.API;
// Explicit one-time setup. Existing databases are never cleared or reseeded.
public static class LocalWorkspace
{
    public static async Task CreateAsync(string connection, string root)
    {
        var source = new NpgsqlConnectionStringBuilder(connection);
        if (source.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Подготовка разрешена только на локальном PostgreSQL.");
        var password = Environment.GetEnvironmentVariable("CLINIC_INITIAL_PASSWORD") ?? "";
        if (InputRules.Error("password", password)is { } error)
            throw new InvalidOperationException("CLINIC_INITIAL_PASSWORD: " + error);
        var target = new NpgsqlConnectionStringBuilder(connection)
        {
            Database = "clinic_local"
        };
        var owner = new NpgsqlConnectionStringBuilder(connection)
        {
            Database = "postgres"
        };
        await using (var server = new NpgsqlConnection(owner.ConnectionString))
        {
            await server.OpenAsync();
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = 'clinic_local'", server);
            if (await exists.ExecuteScalarAsync()is not null)
                throw new InvalidOperationException("clinic_local уже существует. Существующие данные сохранены.");
            await using var create = new NpgsqlCommand("CREATE DATABASE clinic_local", server);
            await create.ExecuteNonQueryAsync();
        }

        await using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>().UseNpgsql(target.ConnectionString).Options);
        var schema = db.Database.GenerateCreateScript().Replace("CREATE SEQUENCE ", "CREATE SEQUENCE IF NOT EXISTS ");
        await db.Database.ExecuteSqlRawAsync(schema);
        await RuntimeSchema.EnsureAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await FillAsync(db, password);
        await transaction.CommitAsync();
        // Only switch the client after a complete, successful setup.
        var configPath = Path.Combine(root, "api.local.json");
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        var privateFolder = Path.Combine(root, "Backups");
        Directory.CreateDirectory(privateFolder);
        File.Copy(configPath, Path.Combine(privateFolder, "connection-before-cleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
        settings["ConnectionStrings"]!["Clinic"] = target.ConnectionString;
        await File.WriteAllTextAsync(configPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Готово: clinic_local. Старая база и настройки сохранены. Логины: m.smirnova, e.volkova, a.ivanova, n.belov.");
    }

    private static async Task FillAsync(MyDbContext db, string password)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var permissions = typeof(Perm).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.Name != "ClaimType").Select(f => new Permission { Code = (string)f.GetRawConstantValue()!, Name = Catalog.PermissionName((string)f.GetRawConstantValue()!) }).ToList();
        Role Role(string code, string name, params string[] allowed) => new()
        {
            Code = code,
            Name = name,
            PermissionCodes = permissions.Where(p => allowed.Contains(p.Code)).ToList()
        };
        var patientRole = Role("patient", "Пациент", Perm.OwnView, Perm.OwnBook);
        var registrarRole = Role("registrar", "Регистратор", Perm.PatientView, Perm.PatientManage, Perm.DocumentManage, Perm.ScheduleManage, Perm.AppointmentManage, Perm.ServiceRender, Perm.InvoiceManage);
        var doctorRole = Role("doctor", "Врач", Perm.PatientView, Perm.DoctorSchedule, Perm.MedcardView, Perm.VisitManage, Perm.ReferralIssue, Perm.ReferralView, Perm.ReferralResult);
        var adminRole = new Role
        {
            Code = "admin",
            Name = "Администратор",
            PermissionCodes = permissions
        };
        db.AddRange(patientRole, registrarRole, doctorRole, adminRole);
        var doctorPost = new Post
        {
            Name = "Врач"
        };
        var registrarPost = new Post
        {
            Name = "Медицинский регистратор"
        };
        var adminPost = new Post
        {
            Name = "Администратор системы"
        };
        var specialties = new[]
        {
            new Specialty
            {
                Name = "Терапия"
            },
            new Specialty
            {
                Name = "Кардиология"
            },
            new Specialty
            {
                Name = "Неврология"
            }
        };
        var doctors = new[]
        {
            new Employee
            {
                LastName = "Иванова",
                FirstName = "Анна",
                MiddleName = "Сергеевна",
                Post = doctorPost,
                Specialty = specialties[0],
                Phone = "+7 (900) 100-20-01",
                IsActive = true
            },
            new Employee
            {
                LastName = "Соколов",
                FirstName = "Дмитрий",
                MiddleName = "Андреевич",
                Post = doctorPost,
                Specialty = specialties[1],
                Phone = "+7 (900) 100-20-02",
                IsActive = true
            },
            new Employee
            {
                LastName = "Морозова",
                FirstName = "Елена",
                MiddleName = "Викторовна",
                Post = doctorPost,
                Specialty = specialties[2],
                Phone = "+7 (900) 100-20-03",
                IsActive = true
            }
        };
        var registrar = new Employee
        {
            LastName = "Волкова",
            FirstName = "Екатерина",
            MiddleName = "Александровна",
            Post = registrarPost,
            IsActive = true
        };
        var admin = new Employee
        {
            LastName = "Белов",
            FirstName = "Никита",
            MiddleName = "Олегович",
            Post = adminPost,
            IsActive = true
        };
        db.AddRange(doctors);
        db.AddRange(registrar, admin);
        var names = new (string Last, string First, string Middle, char Gender)[]
        {
            ("Смирнова", "Мария", "Алексеевна", 'Ж'),
            ("Кузнецов", "Алексей", "Петрович", 'М'),
            ("Лебедева", "Ольга", "Сергеевна", 'Ж'),
            ("Попов", "Илья", "Дмитриевич", 'М'),
            ("Новикова", "Анастасия", "Игоревна", 'Ж'),
            ("Васильев", "Михаил", "Андреевич", 'М'),
            ("Павлова", "Софья", "Романовна", 'Ж'),
            ("Фёдоров", "Артём", "Николаевич", 'М')
        };
        var patients = names.Select((n, i) => new Patient { LastName = n.Last, FirstName = n.First, MiddleName = n.Middle, Gender = n.Gender, BirthDate = new DateOnly(1986 + i * 2, i + 1, 10 + i), Phone = $"+7 (900) 200-30-{i + 1:00}", Address = $"г. Пермь, ул. Лесная, д. {12 + i}, кв. {10 + i}", IsActive = true }).ToArray();
        var insurance = new InsuranceCompany
        {
            Name = "Медицинское страхование",
            IsActive = true
        };
        var district = new District
        {
            Number = "1",
            Name = "Центральный участок"
        };
        for (var i = 0; i < patients.Length; i++)
        {
            db.Add(new PatientPolicy { Patient = patients[i], Kind = "ОМС", Number = $"590000000000{i + 1:0000}", Company = insurance, ValidFrom = today.AddYears(-1), IsActive = true });
            db.Add(new PatientAttachment { Patient = patients[i], Employee = doctors[0], District = district, AttachedFrom = today.AddMonths(-3) });
        }

        var hash = Passwords.Hash(password);
        Account StaffAccount(string login, Employee person, Role role) => new()
        {
            Login = login,
            Employee = person,
            Role = role,
            PasswordHash = hash,
            IsActive = true,
            UserTheme = "light"
        };
        db.AddRange(StaffAccount("a.ivanova", doctors[0], doctorRole), StaffAccount("d.sokolov", doctors[1], doctorRole), StaffAccount("e.morozova", doctors[2], doctorRole), StaffAccount("e.volkova", registrar, registrarRole), StaffAccount("n.belov", admin, adminRole));
        db.Add(new Account { Login = "m.smirnova", Patient = patients[0], Role = patientRole, PasswordHash = hash, IsActive = true, UserTheme = "light" });
        var group = new ServiceGroup
        {
            Name = "Консультации и диагностика"
        };
        var services = new[]
        {
            new Service
            {
                Name = "Приём врача-терапевта",
                Group = group,
                Price = 1800,
                IsActive = true
            },
            new Service
            {
                Name = "Приём врача-кардиолога",
                Group = group,
                Price = 2400,
                IsActive = true
            },
            new Service
            {
                Name = "Приём врача-невролога",
                Group = group,
                Price = 2200,
                IsActive = true
            },
            new Service
            {
                Name = "Общий анализ крови",
                Group = group,
                Price = 650,
                IsActive = true
            },
            new Service
            {
                Name = "Электрокардиография",
                Group = group,
                Price = 1200,
                IsActive = true
            }
        };
        var rooms = new[]
        {
            new Room
            {
                Number = "101",
                Floor = 1
            },
            new Room
            {
                Number = "203",
                Floor = 2
            },
            new Room
            {
                Number = "205",
                Floor = 2
            }
        };
        var cash = new PayMethod
        {
            Name = "Наличные"
        };
        var card = new PayMethod
        {
            Name = "Банковская карта"
        };
        db.AddRange(services);
        db.AddRange(cash, card, new PayMethod { Name = "ДМС", IsInsurance = true });
        db.AddRange(new DocumentType { Name = "Согласие на обработку данных" }, new DocumentType { Name = "Договор платных услуг" }, new DocumentType { Name = "Справка" });
        db.AddRange(new Diagnosis { Code = "Z00.0", Name = "Общий медицинский осмотр" }, new Diagnosis { Code = "J06.9", Name = "Острая инфекция верхних дыхательных путей" });
        db.Add(new Drug { Name = "Парацетамол" });
        for (var day = -7; day <= 14; day++)
        {
            for (var d = 0; d < doctors.Length; d++)
            {
                var schedule = new Schedule
                {
                    Employee = doctors[d],
                    Room = rooms[d],
                    WorkDate = today.AddDays(day),
                    StartTime = new(9, 0),
                    EndTime = new(16, 0),
                    SlotMinutes = 30
                };
                db.Add(schedule);
                if (day is < -4 or > 3)
                    continue;
                for (var slot = 0; slot < (day == 0 ? 3 : 1); slot++)
                {
                    var patient = patients[(d * 2 + slot + Math.Abs(day)) % patients.Length];
                    var appointment = new Appointment
                    {
                        Schedule = schedule,
                        Patient = patient,
                        StartTime = new(9 + slot, 0),
                        Status = day < 0 ? "завершена" : "запланирована",
                        Source = "регистратура",
                        CreatedAt = DateTime.Today.AddDays(-10)
                    };
                    db.Add(appointment);
                    if (day >= 0)
                        continue;
                    var visit = new Visit
                    {
                        Appointment = appointment,
                        VisitedAt = schedule.WorkDate.ToDateTime(appointment.StartTime),
                        IsClosed = true,
                        DiagnosisCode = "Z00.0",
                        Complaints = "Плановый профилактический осмотр.",
                        ObjectiveStatus = "Общее состояние удовлетворительное.",
                        Recommendations = "Результаты осмотра обсуждены с пациентом. Повторный плановый осмотр через год."
                    };
                    var invoice = new Invoice
                    {
                        Patient = patient,
                        CreatedAt = visit.VisitedAt,
                        Status = day == -1 ? "не оплачен" : "оплачен",
                        Method = day == -1 ? null : card,
                        PaidAt = day == -1 ? null : visit.VisitedAt.AddMinutes(45)
                    };
                    db.Add(new VisitService { Visit = visit, Service = services[d], Quantity = 1, Price = services[d].Price, RenderedAt = visit.VisitedAt, Invoice = invoice });
                    if (d == 0)
                        db.Add(new Referral { Visit = visit, Service = services[3], Status = day < -2 ? "выполнено" : "выдано", Executor = day < -2 ? doctors[0] : null, DoneAt = day < -2 ? visit.VisitedAt.AddHours(2) : null, Result = day < -2 ? "Исследование выполнено. Заключение доступно в медицинской карте." : null });
                }
            }
        }

        db.Add(new Notification { Patient = patients[0], CreatedAt = DateTime.Now, Message = "Вы прикреплены к участку № 1. Участковый врач — Иванова Анна Сергеевна.", IsRead = false });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notify_appointment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN INSERT INTO notification(patient_id,created_at,message,is_read)
            VALUES(NEW.patient_id,now(),'Запись на приём: '||NEW.status,false); RETURN NEW; END $$;
            CREATE TRIGGER notify_appointment AFTER INSERT OR UPDATE ON appointment
            FOR EACH ROW EXECUTE FUNCTION notify_appointment();
            """);
    }
}
