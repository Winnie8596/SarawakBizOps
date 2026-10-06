using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.DTOs.Auth;
using SarawakBizOps.Api.Models.Entities;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Auth;

public class AuthService : IAuthService
{
    // Two tabs (or a double-clicked refresh) can present the same refresh token at once. The loser of that race
    // presents a token rotated a moment ago; within this window it still gets a session instead of being treated
    // as a stolen copy.
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

    // A real hash of a random password, computed once, for the "no such account" path (see LoginAsync).
    private static string? _dummyPasswordHash;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        IConfiguration configuration,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _configuration = configuration;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AuthSession?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            // Same response whether the user doesn't exist or is inactive — don't leak which case it was. Hash the
            // password anyway, so this answer takes as long as a wrong password and response times do not reveal
            // which emails have accounts.
            _dummyPasswordHash ??= _userManager.PasswordHasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString());
            _userManager.PasswordHasher.VerifyHashedPassword(user ?? new ApplicationUser(), _dummyPasswordHash, request.Password);
            return null;
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return null;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        // Housekeeping: this user's finished sessions are of no further use.
        await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        return await StartSessionAsync(user, now, now.AddHours(Math.Max(1, _options.RefreshTokenHours)), ct);
    }

    public async Task<AuthSession?> RefreshAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var stored = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            return null;
        }

        // Rotate: only the first request to present this token moves it on (a conditional UPDATE, like stock).
        var rotatedHere = stored.RotatedAt is null && await _db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RotatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RotatedAt, now), ct) == 1;

        if (!rotatedHere)
        {
            var rotatedAt = stored.RotatedAt ?? now; // null: a concurrent request rotated it a moment ago
            if (now - rotatedAt > RotationGrace)
            {
                // An old token came back long after its replacement was issued: someone holds a copy. End every
                // session of this user; the real owner signs in again.
                var revoked = await _db.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
                _logger.LogWarning(
                    "Rotated refresh token {TokenId} of user {UserId} was presented again; revoked {Count} session(s).",
                    stored.Id, stored.UserId, revoked);
                return null;
            }
        }

        var user = await _userManager.FindByIdAsync(stored.UserId);
        if (user is null || !user.IsActive || user.SecurityStamp != stored.SecurityStamp)
        {
            return null;
        }

        // The new token keeps the session's original end, so refreshing never extends a sign-in.
        return await StartSessionAsync(user, now, stored.ExpiresAt, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = Hash(refreshToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        await _db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        return new CurrentUserResponse
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = await _userManager.GetRolesAsync(user)
        };
    }

    public async Task<ServiceResult> ChangePasswordAsync(string userId, ChangePasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return ServiceResult.Fail(ServiceErrorKind.NotFound, "User not found.");
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return ServiceResult.Fail(ServiceErrorKind.Validation,
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        return ServiceResult.Ok();
    }

    private async Task<AuthSession> StartSessionAsync(
        ApplicationUser user, DateTime now, DateTime sessionEndsAt, CancellationToken ct)
    {
        // 256 random bits; only the hash is stored.
        var refreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            SecurityStamp = user.SecurityStamp ?? string.Empty,
            CreatedAt = now,
            ExpiresAt = sessionEndsAt
        });
        await _db.SaveChangesAsync(ct);

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = GenerateJwtToken(user, roles);

        return new AuthSession(new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = roles
        }, refreshToken, sessionEndsAt);
    }

    private static string Hash(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private (string token, DateTime expiresAtUtc) GenerateJwtToken(ApplicationUser user, IList<string> roles)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var keyValue = jwtSection["Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it via user-secrets.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new("fullName", user.FullName),
            // Checked on every request (see JwtUserValidator) so that deactivating a user,
            // changing their role or changing their password revokes existing tokens.
            new(JwtUserValidator.SecurityStampClaim, user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Short, because a stolen access token works until it expires; the refresh cookie renews it quietly.
        var minutes = jwtSection.GetValue<int?>("AccessTokenMinutes") ?? 15;
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
