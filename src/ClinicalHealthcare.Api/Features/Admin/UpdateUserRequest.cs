using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>Request body for <c>PATCH /admin/users/{id}</c>. All fields optional — only non-null fields are applied.</summary>
public sealed class UpdateUserRequest
{
    [MaxLength(100)]
    public string? FirstName { get; init; }

    [MaxLength(100)]
    public string? LastName { get; init; }

    public bool? IsActive { get; init; }
}
