using System;
using System.Collections.Generic;
using Clinic.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Context;

public partial class MyDbContext : DbContext
{
    public MyDbContext()
    {
    }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<Appointment> Appointments { get; set; }

    public virtual DbSet<Audit> Audits { get; set; }

    public virtual DbSet<BackupLog> BackupLogs { get; set; }

    public virtual DbSet<Diagnosis> Diagnoses { get; set; }

    public virtual DbSet<District> Districts { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentType> DocumentTypes { get; set; }

    public virtual DbSet<Drug> Drugs { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<InsuranceCompany> InsuranceCompanies { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Patient> Patients { get; set; }

    public virtual DbSet<PatientAttachment> PatientAttachments { get; set; }

    public virtual DbSet<PatientPolicy> PatientPolicies { get; set; }

    public virtual DbSet<PayMethod> PayMethods { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<Prescription> Prescriptions { get; set; }

    public virtual DbSet<Referral> Referrals { get; set; }

    public virtual DbSet<ReferralFile> ReferralFiles { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<Schedule> Schedules { get; set; }

    public virtual DbSet<Service> Services { get; set; }

    public virtual DbSet<ServiceGroup> ServiceGroups { get; set; }

    public virtual DbSet<Specialty> Specialties { get; set; }

    public virtual DbSet<VAttendance> VAttendances { get; set; }

    public virtual DbSet<VInvoice> VInvoices { get; set; }

    public virtual DbSet<VMorbidity> VMorbidities { get; set; }

    public virtual DbSet<VRevenueDay> VRevenueDays { get; set; }

    public virtual DbSet<VScheduleLoad> VScheduleLoads { get; set; }

    public virtual DbSet<Visit> Visits { get; set; }

    public virtual DbSet<VisitService> VisitServices { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseLazyLoadingProxies().UseNpgsql("Host=localhost;Port=5432;Database=Clinic;Username=postgres;Password=postgres");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("account_pkey");

            entity.ToTable("account");

            entity.HasIndex(e => e.EmployeeId, "account_employee_id_key").IsUnique();

            entity.HasIndex(e => e.Login, "account_login_key").IsUnique();

            entity.HasIndex(e => e.PatientId, "account_patient_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastLogin)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_login");
            entity.Property(e => e.Login)
                .HasMaxLength(50)
                .HasColumnName("login");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UserTheme)
                .HasMaxLength(10)
                .HasDefaultValueSql("'light'::character varying")
                .HasColumnName("user_theme");

            entity.HasOne(d => d.Employee).WithOne(p => p.Account)
                .HasForeignKey<Account>(d => d.EmployeeId)
                .HasConstraintName("account_employee_id_fkey");

            entity.HasOne(d => d.Patient).WithOne(p => p.Account)
                .HasForeignKey<Account>(d => d.PatientId)
                .HasConstraintName("account_patient_id_fkey");

            entity.HasOne(d => d.Role).WithMany(p => p.Accounts)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("account_role_id_fkey");
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("appointment_pkey");

            entity.ToTable("appointment");

            entity.HasIndex(e => e.PatientId, "ix_appointment_patient");

            entity.HasIndex(e => new { e.ScheduleId, e.StartTime }, "ux_appointment_slot")
                .IsUnique()
                .HasFilter("((status)::text <> 'отменена'::text)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
            entity.Property(e => e.Source)
                .HasMaxLength(20)
                .HasColumnName("source");
            entity.Property(e => e.StartTime).HasColumnName("start_time");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'запланирована'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Patient).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("appointment_patient_id_fkey");

            entity.HasOne(d => d.Schedule).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.ScheduleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("appointment_schedule_id_fkey");
        });

        modelBuilder.Entity<Audit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_pkey");

            entity.ToTable("audit");

            entity.HasIndex(e => e.AccountId, "ix_audit_account");

            entity.HasIndex(e => e.PatientId, "ix_audit_patient");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.ActedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("acted_at");
            entity.Property(e => e.Action)
                .HasMaxLength(20)
                .HasColumnName("action");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.RecordId).HasColumnName("record_id");
            entity.Property(e => e.TableName)
                .HasMaxLength(50)
                .HasColumnName("table_name");

            entity.HasOne(d => d.Account).WithMany(p => p.Audits)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("audit_account_id_fkey");

            entity.HasOne(d => d.Patient).WithMany(p => p.Audits)
                .HasForeignKey(d => d.PatientId)
                .HasConstraintName("audit_patient_id_fkey");
        });

        modelBuilder.Entity<BackupLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("backup_log_pkey");

            entity.ToTable("backup_log");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Action)
                .HasMaxLength(20)
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");

            entity.HasOne(d => d.Account).WithMany(p => p.BackupLogs)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("backup_log_account_id_fkey");
        });

        modelBuilder.Entity<Diagnosis>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("diagnosis_pkey");

            entity.ToTable("diagnosis");

            entity.Property(e => e.Code)
                .HasMaxLength(10)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(300)
                .HasColumnName("name");
        });

        modelBuilder.Entity<District>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("district_pkey");

            entity.ToTable("district");

            entity.HasIndex(e => e.Number, "district_number_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Number)
                .HasMaxLength(10)
                .HasColumnName("number");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("document_pkey");

            entity.ToTable("document");

            entity.HasIndex(e => e.Number, "document_number_key").IsUnique();

            entity.HasIndex(e => e.PatientId, "ix_document_patient");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.IssuedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("issued_at");
            entity.Property(e => e.IssuedBy).HasColumnName("issued_by");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasDefaultValueSql("('Д-'::text || lpad((nextval('document_number_seq'::regclass))::text, 6, '0'::text))")
                .HasColumnName("number");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.TypeId).HasColumnName("type_id");
            entity.Property(e => e.ValidUntil).HasColumnName("valid_until");
            entity.Property(e => e.VisitId).HasColumnName("visit_id");

            entity.HasOne(d => d.IssuedByNavigation).WithMany(p => p.Documents)
                .HasForeignKey(d => d.IssuedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("document_issued_by_fkey");

            entity.HasOne(d => d.Patient).WithMany(p => p.Documents)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("document_patient_id_fkey");

            entity.HasOne(d => d.Type).WithMany(p => p.Documents)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("document_type_id_fkey");

            entity.HasOne(d => d.Visit).WithMany(p => p.Documents)
                .HasForeignKey(d => d.VisitId)
                .HasConstraintName("document_visit_id_fkey");
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("document_type_pkey");

            entity.ToTable("document_type");

            entity.HasIndex(e => e.Name, "document_type_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Drug>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("drug_pkey");

            entity.ToTable("drug");

            entity.HasIndex(e => e.Name, "drug_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("employee_pkey");

            entity.ToTable("employee");

            entity.HasIndex(e => e.SpecialtyId, "ix_employee_specialty");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FirstName)
                .HasMaxLength(50)
                .HasColumnName("first_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastName)
                .HasMaxLength(50)
                .HasColumnName("last_name");
            entity.Property(e => e.MiddleName)
                .HasMaxLength(50)
                .HasColumnName("middle_name");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
            entity.Property(e => e.PostId).HasColumnName("post_id");
            entity.Property(e => e.SpecialtyId).HasColumnName("specialty_id");

            entity.HasOne(d => d.Post).WithMany(p => p.Employees)
                .HasForeignKey(d => d.PostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("employee_post_id_fkey");

            entity.HasOne(d => d.Specialty).WithMany(p => p.Employees)
                .HasForeignKey(d => d.SpecialtyId)
                .HasConstraintName("employee_specialty_id_fkey");
        });

        modelBuilder.Entity<InsuranceCompany>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("insurance_company_pkey");

            entity.ToTable("insurance_company");

            entity.HasIndex(e => e.Name, "insurance_company_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoice_pkey");

            entity.ToTable("invoice");

            entity.HasIndex(e => e.PatientId, "ix_invoice_patient");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.MethodId).HasColumnName("method_id");
            entity.Property(e => e.PaidAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.PolicyId).HasColumnName("policy_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'не оплачен'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Method).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.MethodId)
                .HasConstraintName("invoice_method_id_fkey");

            entity.HasOne(d => d.Patient).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("invoice_patient_id_fkey");

            entity.HasOne(d => d.Policy).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.PolicyId)
                .HasConstraintName("invoice_policy_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_pkey");

            entity.ToTable("notification");

            entity.HasIndex(e => new { e.PatientId, e.IsRead }, "ix_notification_patient");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.Message)
                .HasMaxLength(300)
                .HasColumnName("message");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");

            entity.HasOne(d => d.Patient).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("notification_patient_id_fkey");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("patient_pkey");

            entity.ToTable("patient");

            entity.HasIndex(e => e.CardNumber, "patient_card_number_key").IsUnique();

            entity.HasIndex(e => e.Snils, "patient_snils_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(200)
                .HasColumnName("address");
            entity.Property(e => e.BirthDate).HasColumnName("birth_date");
            entity.Property(e => e.CardNumber)
                .HasMaxLength(20)
                .HasDefaultValueSql("('П-'::text || lpad((nextval('patient_card_seq'::regclass))::text, 6, '0'::text))")
                .HasColumnName("card_number");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("CURRENT_DATE")
                .HasColumnName("created_date");
            entity.Property(e => e.FirstName)
                .HasMaxLength(50)
                .HasColumnName("first_name");
            entity.Property(e => e.Gender)
                .HasMaxLength(1)
                .HasColumnName("gender");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastName)
                .HasMaxLength(50)
                .HasColumnName("last_name");
            entity.Property(e => e.MiddleName)
                .HasMaxLength(50)
                .HasColumnName("middle_name");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
            entity.Property(e => e.Snils)
                .HasMaxLength(14)
                .HasColumnName("snils");
        });

        modelBuilder.Entity<PatientAttachment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("patient_attachment_pkey");

            entity.ToTable("patient_attachment");

            entity.HasIndex(e => e.EmployeeId, "ix_attachment_employee");

            entity.HasIndex(e => e.PatientId, "ux_attachment_current")
                .IsUnique()
                .HasFilter("(attached_to IS NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttachedFrom)
                .HasDefaultValueSql("CURRENT_DATE")
                .HasColumnName("attached_from");
            entity.Property(e => e.AttachedTo).HasColumnName("attached_to");
            entity.Property(e => e.DistrictId).HasColumnName("district_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");

            entity.HasOne(d => d.District).WithMany(p => p.PatientAttachments)
                .HasForeignKey(d => d.DistrictId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("patient_attachment_district_id_fkey");

            entity.HasOne(d => d.Employee).WithMany(p => p.PatientAttachments)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("patient_attachment_employee_id_fkey");

            entity.HasOne(d => d.Patient).WithOne(p => p.PatientAttachment)
                .HasForeignKey<PatientAttachment>(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("patient_attachment_patient_id_fkey");
        });

        modelBuilder.Entity<PatientPolicy>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("patient_policy_pkey");

            entity.ToTable("patient_policy");

            entity.HasIndex(e => e.PatientId, "ix_policy_patient");

            entity.HasIndex(e => new { e.Kind, e.Number }, "patient_policy_kind_number_key").IsUnique();

            entity.HasIndex(e => new { e.PatientId, e.Kind }, "ux_policy_active")
                .IsUnique()
                .HasFilter("is_active");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Kind)
                .HasMaxLength(3)
                .HasColumnName("kind");
            entity.Property(e => e.Number)
                .HasMaxLength(20)
                .HasColumnName("number");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to");

            entity.HasOne(d => d.Company).WithMany(p => p.PatientPolicies)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("patient_policy_company_id_fkey");

            entity.HasOne(d => d.Patient).WithMany(p => p.PatientPolicies)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("patient_policy_patient_id_fkey");
        });

        modelBuilder.Entity<PayMethod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pay_method_pkey");

            entity.ToTable("pay_method");

            entity.HasIndex(e => e.Name, "pay_method_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsInsurance).HasColumnName("is_insurance");
            entity.Property(e => e.Name)
                .HasMaxLength(30)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("permission_pkey");

            entity.ToTable("permission");

            entity.Property(e => e.Code)
                .HasMaxLength(40)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("post_pkey");

            entity.ToTable("post");

            entity.HasIndex(e => e.Name, "post_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("prescription_pkey");

            entity.ToTable("prescription");

            entity.HasIndex(e => e.VisitId, "ix_prescription_visit");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Dosage)
                .HasMaxLength(100)
                .HasColumnName("dosage");
            entity.Property(e => e.DrugId).HasColumnName("drug_id");
            entity.Property(e => e.Instruction).HasColumnName("instruction");
            entity.Property(e => e.IsRecipe).HasColumnName("is_recipe");
            entity.Property(e => e.VisitId).HasColumnName("visit_id");

            entity.HasOne(d => d.Drug).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.DrugId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("prescription_drug_id_fkey");

            entity.HasOne(d => d.Visit).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.VisitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("prescription_visit_id_fkey");
        });

        modelBuilder.Entity<Referral>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("referral_pkey");

            entity.ToTable("referral");

            entity.HasIndex(e => e.Status, "ix_referral_status");

            entity.HasIndex(e => e.VisitId, "ix_referral_visit");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DoneAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("done_at");
            entity.Property(e => e.ExecutorId).HasColumnName("executor_id");
            entity.Property(e => e.Result).HasColumnName("result");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'выдано'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.VisitId).HasColumnName("visit_id");

            entity.HasOne(d => d.Executor).WithMany(p => p.Referrals)
                .HasForeignKey(d => d.ExecutorId)
                .HasConstraintName("referral_executor_id_fkey");

            entity.HasOne(d => d.Service).WithMany(p => p.Referrals)
                .HasForeignKey(d => d.ServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("referral_service_id_fkey");

            entity.HasOne(d => d.Visit).WithMany(p => p.Referrals)
                .HasForeignKey(d => d.VisitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("referral_visit_id_fkey");
        });

        modelBuilder.Entity<ReferralFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("referral_file_pkey");

            entity.ToTable("referral_file");

            entity.HasIndex(e => e.ReferralId, "ix_referral_file_ref");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContentType)
                .HasMaxLength(100)
                .HasColumnName("content_type");
            entity.Property(e => e.Data).HasColumnName("data");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.ReferralId).HasColumnName("referral_id");
            entity.Property(e => e.SizeBytes)
                .HasComputedColumnSql("octet_length(data)", true)
                .HasColumnName("size_bytes");
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("uploaded_at");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");

            entity.HasOne(d => d.Referral).WithMany(p => p.ReferralFiles)
                .HasForeignKey(d => d.ReferralId)
                .HasConstraintName("referral_file_referral_id_fkey");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.ReferralFiles)
                .HasForeignKey(d => d.UploadedBy)
                .HasConstraintName("referral_file_uploaded_by_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("role_pkey");

            entity.ToTable("role");

            entity.HasIndex(e => e.Code, "role_code_key").IsUnique();

            entity.HasIndex(e => e.Name, "role_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(20)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");

            entity.HasMany(d => d.PermissionCodes).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionCode")
                        .HasConstraintName("role_permission_permission_code_fkey"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("role_permission_role_id_fkey"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionCode").HasName("role_permission_pkey");
                        j.ToTable("role_permission");
                        j.IndexerProperty<int>("RoleId").HasColumnName("role_id");
                        j.IndexerProperty<string>("PermissionCode")
                            .HasMaxLength(40)
                            .HasColumnName("permission_code");
                    });
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("room_pkey");

            entity.ToTable("room");

            entity.HasIndex(e => e.Number, "room_number_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Floor).HasColumnName("floor");
            entity.Property(e => e.Number)
                .HasMaxLength(10)
                .HasColumnName("number");
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("schedule_pkey");

            entity.ToTable("schedule");

            entity.HasIndex(e => e.WorkDate, "ix_schedule_date");

            entity.HasIndex(e => new { e.EmployeeId, e.WorkDate }, "schedule_employee_id_work_date_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.SlotMinutes).HasColumnName("slot_minutes");
            entity.Property(e => e.StartTime).HasColumnName("start_time");
            entity.Property(e => e.WorkDate).HasColumnName("work_date");

            entity.HasOne(d => d.Employee).WithMany(p => p.Schedules)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("schedule_employee_id_fkey");

            entity.HasOne(d => d.Room).WithMany(p => p.Schedules)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("schedule_room_id_fkey");
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("service_pkey");

            entity.ToTable("service");

            entity.HasIndex(e => e.Name, "service_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.GroupId).HasColumnName("group_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");

            entity.HasOne(d => d.Group).WithMany(p => p.Services)
                .HasForeignKey(d => d.GroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("service_group_id_fkey");
        });

        modelBuilder.Entity<ServiceGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("service_group_pkey");

            entity.ToTable("service_group");

            entity.HasIndex(e => e.Name, "service_group_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Specialty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("specialty_pkey");

            entity.ToTable("specialty");

            entity.HasIndex(e => e.Name, "specialty_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<VAttendance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_attendance");

            entity.Property(e => e.Cancelled).HasColumnName("cancelled");
            entity.Property(e => e.Completed).HasColumnName("completed");
            entity.Property(e => e.Day).HasColumnName("day");
            entity.Property(e => e.NoShow).HasColumnName("no_show");
            entity.Property(e => e.Planned).HasColumnName("planned");
        });

        modelBuilder.Entity<VInvoice>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_invoice");

            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MethodId).HasColumnName("method_id");
            entity.Property(e => e.PaidAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.PolicyId).HasColumnName("policy_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.Total)
                .HasPrecision(10, 2)
                .HasColumnName("total");
        });

        modelBuilder.Entity<VMorbidity>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_morbidity");

            entity.Property(e => e.DiagnosisCode)
                .HasMaxLength(10)
                .HasColumnName("diagnosis_code");
            entity.Property(e => e.DiagnosisName)
                .HasMaxLength(300)
                .HasColumnName("diagnosis_name");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.Visits).HasColumnName("visits");
        });

        modelBuilder.Entity<VRevenueDay>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_revenue_day");

            entity.Property(e => e.Day).HasColumnName("day");
            entity.Property(e => e.Invoices).HasColumnName("invoices");
            entity.Property(e => e.Method)
                .HasMaxLength(30)
                .HasColumnName("method");
            entity.Property(e => e.Total)
                .HasPrecision(12, 2)
                .HasColumnName("total");
        });

        modelBuilder.Entity<VScheduleLoad>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_schedule_load");

            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
            entity.Property(e => e.SlotsBooked).HasColumnName("slots_booked");
            entity.Property(e => e.SlotsTotal).HasColumnName("slots_total");
            entity.Property(e => e.WorkDate).HasColumnName("work_date");
        });

        modelBuilder.Entity<Visit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("visit_pkey");

            entity.ToTable("visit");

            entity.HasIndex(e => e.DiagnosisCode, "ix_visit_diagnosis");

            entity.HasIndex(e => e.AppointmentId, "visit_appointment_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Anamnesis).HasColumnName("anamnesis");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.Complaints).HasColumnName("complaints");
            entity.Property(e => e.DiagnosisCode)
                .HasMaxLength(10)
                .HasColumnName("diagnosis_code");
            entity.Property(e => e.IsClosed).HasColumnName("is_closed");
            entity.Property(e => e.ObjectiveStatus).HasColumnName("objective_status");
            entity.Property(e => e.Recommendations).HasColumnName("recommendations");
            entity.Property(e => e.VisitedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("visited_at");

            entity.HasOne(d => d.Appointment).WithOne(p => p.Visit)
                .HasForeignKey<Visit>(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("visit_appointment_id_fkey");

            entity.HasOne(d => d.DiagnosisCodeNavigation).WithMany(p => p.Visits)
                .HasForeignKey(d => d.DiagnosisCode)
                .HasConstraintName("visit_diagnosis_code_fkey");
        });

        modelBuilder.Entity<VisitService>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("visit_service_pkey");

            entity.ToTable("visit_service");

            entity.HasIndex(e => e.InvoiceId, "ix_vs_invoice");

            entity.HasIndex(e => e.VisitId, "ix_vs_visit");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
            entity.Property(e => e.RenderedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("rendered_at");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.VisitId).HasColumnName("visit_id");

            entity.HasOne(d => d.Invoice).WithMany(p => p.VisitServices)
                .HasForeignKey(d => d.InvoiceId)
                .HasConstraintName("visit_service_invoice_id_fkey");

            entity.HasOne(d => d.Service).WithMany(p => p.VisitServices)
                .HasForeignKey(d => d.ServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("visit_service_service_id_fkey");

            entity.HasOne(d => d.Visit).WithMany(p => p.VisitServices)
                .HasForeignKey(d => d.VisitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("visit_service_visit_id_fkey");
        });
        modelBuilder.HasSequence("document_number_seq");
        modelBuilder.HasSequence("patient_card_seq");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
