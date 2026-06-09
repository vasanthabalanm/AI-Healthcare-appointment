using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>Request body for <c>POST /admin/users</c>.</summary>
public sealed class CreateUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    /// <summary>Valid values: "admin" | "staff"</summary>
    [Required]
    public string Role { get; init; } = string.Empty;
}
