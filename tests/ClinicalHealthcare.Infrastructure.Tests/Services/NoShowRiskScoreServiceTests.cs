using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for TASK_022: NoShowRiskScoreService.
/// Covers AC-001 (0–100 range), AC-002 (three components), AC-003 (threshold).
/// Validation strategy from task:
///   - 3 no-shows + lead 12h + no intake → 60+30+10 = 100
///   - 0 no-shows + lead 5 days + intake complete → 0
/// </summary>
public sealed class NoShowRiskScoreServiceTests
{
    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static UserAccount SeedPatient(ApplicationDbContext db)
    {
        var patient = new UserAccount
        {
            Email        = "p@test.com",
            Role         = "patient",
            FirstName    = "P",
            LastName     = "T",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();
        return patient;
    }

    // ── AC-002: all components at maximum → score = 100 (capped) ─────────────

    [Fact]
    public async Task Calculate_3NoShows_LeadTime12h_NoIntake_Returns100()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);

        // Seed a slot for the no-show appointments
        var oldSlot = new Slot { SlotTime = DateTime.UtcNow.AddDays(-10), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(oldSlot);
        db.SaveChanges();

        // 3 prior no-shows → +60 (component 1 capped at 60)
        for (var i = 0; i < 3; i++)
        {
            db.Appointments.Add(new Appointment
            {
                PatientId = patient.Id,
                SlotId    = oldSlot.Id,
                Status    = AppointmentStatus.NoShow,
                BookedAt  = DateTime.UtcNow.AddDays(-10),
            });
        }
        db.SaveChanges();
        // No IntakeRecord — component 3 = +10

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddHours(12); // <24h → +30

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(100, score); // 60 + 30 + 10 = 100
    }

    // ── AC-002: 0 no-shows + >72h lead + intake complete → score = 0 ─────────

    [Fact]
    public async Task Calculate_NoHistory_LongLead_IntakeComplete_Returns0()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);

        // Completed intake record
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId  = patient.Id,
            IsLatest   = true,
            SubmittedAt = DateTime.UtcNow.AddDays(-5),
        });
        db.SaveChanges();

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddDays(5); // >72h → +0

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(0, score);
    }

    // ── AC-002 component 1: no-show count capped at 60 ────────────────────────

    [Fact]
    public async Task Calculate_FiveNoShows_NoShowScoreCappedAt60()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);

        var oldSlot = new Slot { SlotTime = DateTime.UtcNow.AddDays(-5), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(oldSlot);
        db.SaveChanges();

        for (var i = 0; i < 5; i++)
        {
            db.Appointments.Add(new Appointment
            {
                PatientId = patient.Id,
                SlotId    = oldSlot.Id,
                Status    = AppointmentStatus.NoShow,
                BookedAt  = DateTime.UtcNow.AddDays(-5),
            });
        }
        db.SaveChanges();

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddDays(5); // >72h → +0, no intake → +10

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(70, score); // 60 (capped) + 0 + 10 = 70
    }

    // ── AC-002 component 2: lead time 24–72h → +15 ────────────────────────────

    [Fact]
    public async Task Calculate_LeadTime48h_AddsOnly15Points()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        // No no-shows, no intake → only lead time score

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddHours(48); // 24–72h → +15

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(25, score); // 0 + 15 + 10 = 25
    }

    // ── AC-002 component 2: lead time <24h → +30 ──────────────────────────────

    [Fact]
    public async Task Calculate_LeadTimeLessThan24h_Adds30Points()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        // No no-shows, has intake record (component 3 = 0)
        db.IntakeRecords.Add(new IntakeRecord { PatientId = patient.Id, IsLatest = true, SubmittedAt = DateTime.UtcNow });
        db.SaveChanges();

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddHours(6); // <24h → +30

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(30, score); // 0 + 30 + 0 = 30
    }

    // ── AC-002 component 3: no intake → +10 ─────────────────────────────────

    [Fact]
    public async Task Calculate_NoIntakeRecord_Adds10Points()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        // No no-shows, >72h lead — only intake component fires

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddDays(5); // >72h → +0

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.Equal(10, score); // 0 + 0 + 10 = 10
    }

    // ── AC-001: score is capped at 100 (not exceeding) ───────────────────────

    [Fact]
    public async Task Calculate_ScoreNeverExceeds100()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);

        var oldSlot = new Slot { SlotTime = DateTime.UtcNow.AddDays(-3), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(oldSlot);
        db.SaveChanges();

        // 10 no-shows: raw = 200, should cap at 60
        for (var i = 0; i < 10; i++)
        {
            db.Appointments.Add(new Appointment
            {
                PatientId = patient.Id,
                SlotId    = oldSlot.Id,
                Status    = AppointmentStatus.NoShow,
                BookedAt  = DateTime.UtcNow.AddDays(-3),
            });
        }
        db.SaveChanges();

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddHours(1); // <24h → +30

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.InRange(score, 0, 100);
        Assert.Equal(100, score); // 60 + 30 + 10 = 100
    }

    // ── AC-003: IsHighRisk flag via threshold = 70 ────────────────────────────

    [Fact]
    public async Task Calculate_ScoreAtThreshold_IsHighRisk()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);

        // 3 no-shows → 60; lead 48h → 15; no intake → 10. Total = 85 → high-risk
        var oldSlot = new Slot { SlotTime = DateTime.UtcNow.AddDays(-2), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(oldSlot);
        db.SaveChanges();

        for (var i = 0; i < 3; i++)
        {
            db.Appointments.Add(new Appointment
            {
                PatientId = patient.Id,
                SlotId    = oldSlot.Id,
                Status    = AppointmentStatus.NoShow,
                BookedAt  = DateTime.UtcNow.AddDays(-2),
            });
        }
        db.SaveChanges();

        var service  = new NoShowRiskScoreService(db);
        var slotTime = DateTime.UtcNow.AddHours(48); // 24–72h → +15

        var score = await service.CalculateAsync(patient.Id, slotTime);

        Assert.True(score >= 70, $"Expected score ≥ 70 for high-risk, got {score}");
    }
}
