namespace ClinicalHealthcare.Infrastructure.Auth;

/// <summary>
/// Generates signed JWT access tokens for authenticated users.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Creates a JWT signed with <c>JWT_SECRET</c>, valid for 15 minutes.
    /// </summary>
    /// <param name="userId">The user's primary key.</param>
    /// <param name="role">The user's role claim value.</param>
    /// <returns>
    /// A tuple of (<c>token</c> — the encoded JWT string) and
    /// (<c>jti</c> — the unique token identifier used as the Redis allowlist key).
    /// </returns>
    (string token, string jti) GenerateToken(int userId, string role, string firstName = "", string lastName = "");
}
