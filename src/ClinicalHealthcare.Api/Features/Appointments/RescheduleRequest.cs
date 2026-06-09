namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>Request body for PATCH /appointments/{id}/reschedule.</summary>
public sealed record RescheduleRequest(int NewSlotId);
