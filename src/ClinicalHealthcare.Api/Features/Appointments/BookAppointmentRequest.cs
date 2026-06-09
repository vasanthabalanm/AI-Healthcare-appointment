namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Request body for <c>POST /appointments</c>.
/// </summary>
/// <param name="SlotId">The ID of the <see cref="ClinicalHealthcare.Infrastructure.Entities.Slot"/> to book.</param>
public sealed record BookAppointmentRequest(int SlotId);
