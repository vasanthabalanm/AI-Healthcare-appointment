using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Data;

/// <summary>
/// Seeds three default development accounts when the <c>UserAccounts</c> table is empty.
/// Also seeds appointment slots for the next 14 days and one booked appointment for the
/// demo patient account so the patient dashboard and booking calendar show live data.
///
/// Runs only in the Development environment — never in Staging or Production.
///
/// Default credentials:
/// <list type="table">
///   <item><term>admin@clinicalhub.dev</term><description>Admin@1234 — role: admin</description></item>
///   <item><term>staff@clinicalhub.dev</term><description>Staff@1234 — role: staff</description></item>
///   <item><term>patient@clinicalhub.dev</term><description>Patient@1234 — role: patient</description></item>
/// </list>
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<string>>();

        bool hasAccounts = await db.UserAccounts.AnyAsync();

        if (!hasAccounts)
        {
            var seeds = new[]
            {
                new { Email = "admin@clinicalhub.dev",   Password = "Admin@1234",   Role = "admin",   First = "Morgan",  Last = "Blake"   },
                new { Email = "staff@clinicalhub.dev",   Password = "Staff@1234",   Role = "staff",   First = "Jordan",  Last = "Chen"    },
                new { Email = "patient@clinicalhub.dev", Password = "Patient@1234", Role = "patient", First = "Alex",    Last = "Rivera"  },
            };

            foreach (var s in seeds)
            {
                var email = s.Email.ToLowerInvariant();
                db.UserAccounts.Add(new UserAccount
                {
                    Email        = email,
                    PasswordHash = hasher.HashPassword(email, s.Password),
                    Role         = s.Role,
                    IsActive     = true,
                    FirstName    = s.First,
                    LastName     = s.Last,
                    CreatedAt    = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("DevDataSeeder: seeded 3 accounts.");
        }
        else
        {
            logger.LogInformation("DevDataSeeder: accounts already exist — skipping account seed.");
        }

        // ── Seed slots if none exist (using raw SQL to handle RowVersion) ─────
        if (!await db.Slots.AnyAsync())
        {
            var today     = DateTime.UtcNow.Date;
            var slotTimes = new[] { 9, 11, 14 }; // 09:00, 11:00, 14:00 UTC
            var slotCount = 0;

            for (var d = 0; d < 14; d++)
            {
                foreach (var hour in slotTimes)
                {
                    var slotTime = today.AddDays(d).AddHours(hour);
                    // Use raw SQL to insert with explicit RowVersion value
                    await db.Database.ExecuteSqlRawAsync(
                        @"INSERT INTO ""Slots"" (""SlotTime"", ""DurationMinutes"", ""IsAvailable"", ""RowVersion"") 
                          VALUES ({0}, {1}, {2}, {3})",
                        slotTime, 30, true, BitConverter.GetBytes(DateTime.UtcNow.Ticks + slotCount));
                    slotCount++;
                }
            }

            // Book the first slot for the demo patient.
            var patient = await db.UserAccounts.FirstOrDefaultAsync(u => u.Role == "patient");
            var firstSlot = await db.Slots.OrderBy(s => s.SlotTime).FirstOrDefaultAsync();
            if (patient != null && firstSlot != null)
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Slots"" SET ""IsAvailable"" = false WHERE ""Id"" = {0}", firstSlot.Id);
                
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""Appointments"" (""PatientId"", ""SlotId"", ""Status"", ""BookedAt"", ""NoShowRiskScore"", ""RowVersion"") 
                      VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                    patient.Id, firstSlot.Id, (int)AppointmentStatus.Scheduled, DateTime.UtcNow, 
                    0.0m, // NoShowRiskScore default
                    BitConverter.GetBytes(DateTime.UtcNow.Ticks));
            }

            logger.LogInformation(
                "DevDataSeeder: seeded {SlotCount} slots + 1 demo appointment.", slotCount);
        }
        else
        {
            logger.LogInformation("DevDataSeeder: slots already exist — skipping slot seed.");
        }
    }
}
