using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>Request body for <c>POST /auth/register</c>.</summary>
public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Minimum 8 characters. Hashed server-side with PBKDF2 SHA-256 (100k iterations)
    /// via <c>PasswordHasher&lt;string&gt;</c>. Plain-text password is never persisted.
    /// </summary>
    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; init; } = string.Empty;
}
