namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Request body for <c>POST /waitlist</c>.
/// </summary>
/// <param name="PreferredSlotId">
/// Optional ID of a specific <see cref="ClinicalHealthcare.Infrastructure.Entities.Slot"/> the patient
/// is waiting for. <c>null</c> means "any available slot on <paramref name="PreferredSlotDate"/>".
/// </param>
/// <param name="PreferredSlotDate">
/// The date the patient wants to be seen. Must be today or a future date (UTC).
/// </param>
public sealed record JoinWaitlistRequest(int? PreferredSlotId, DateOnly PreferredSlotDate);
