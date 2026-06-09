using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ClinicalHealthcare.Infrastructure.Auth;

/// <summary>
/// Generates HMAC-SHA-256 signed JWT access tokens.
/// Signs using the <c>JWT_SECRET</c> environment variable (fail-fast on startup if missing).
/// Token TTL is exactly 15 minutes (<see cref="TokenExpirySeconds"/> = 900).
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>JWT access-token lifetime in seconds (AC-001: 15 minutes).</summary>
    public const int TokenExpirySeconds = 900;

    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService()
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException(
                "Required environment variable 'JWT_SECRET' is not set. " +
                "Set it before starting the application.");

        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException(
                "JWT_SECRET must be at least 32 bytes (256 bits) for HMAC-SHA-256 security.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <inheritdoc/>
    public (string token, string jti) GenerateToken(int userId, string role, string firstName = "", string lastName = "")
    {
        var jti   = Guid.NewGuid().ToString();
        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var now   = DateTime.UtcNow;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,        userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti,        jti),
            new Claim("role",                             role),
            new Claim(JwtRegisteredClaimNames.GivenName,  firstName),
            new Claim(JwtRegisteredClaimNames.FamilyName, lastName),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var descriptor = new JwtSecurityToken(
            claims:             claims,
            notBefore:          now,
            expires:            now.AddSeconds(TokenExpirySeconds),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(descriptor), jti);
    }
}
