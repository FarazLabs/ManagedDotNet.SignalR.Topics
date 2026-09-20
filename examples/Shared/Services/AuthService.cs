using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ManagedDotNet.SignalR.Topics.Examples.Shared.Services;

// ponytail: hardcoded demo key — replace with a real IdP when this leaves examples/
public static class AuthService
{
    public static class Roles
    {
        public const string User = "User";
        public const string Administrator = "Administrator";
    }

    private const string Issuer = "Auth";
    private const string Audience = "*";
    private const string SigningKey = "7f3A9xK2mQ8vL5pR1tY6wN4cB9sD3hG8";


    public static readonly TokenValidationParameters TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateLifetime = true
    };


    public static string CreateToken(string name, params string[] roles)
    {
        List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, name) };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        JwtSecurityToken token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                TokenValidationParameters.IssuerSigningKey,
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
