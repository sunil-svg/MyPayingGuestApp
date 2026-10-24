using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PgManagement.Api.Users;

public static class UserEndpoints
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async Task<IResult> (
                HttpRequest request,
                PgDbContext database,
                PasswordHasher<PgUser> passwordHasher,
                IDataProtectionProvider protectionProvider,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var fullName = form["fullName"].ToString().Trim();
                var mobileNumber = form["mobileNumber"].ToString().Trim();
                var address = form["address"].ToString().Trim();
                var email = form["email"].ToString().Trim().ToLowerInvariant();
                var aadhaarNumber = form["aadhaarNumber"].ToString().Trim();
                var password = form["password"].ToString();
                var profilePhoto = form.Files.GetFile("profilePhoto");

                var errors = ValidateRegistration(
                    fullName,
                    mobileNumber,
                    address,
                    email,
                    aadhaarNumber,
                    password,
                    profilePhoto);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                if (await database.Users.AnyAsync(user => user.Email == email, cancellationToken))
                {
                    return TypedResults.Conflict(new { message = "An account with this email already exists." });
                }

                var extension = await GetImageExtensionAsync(profilePhoto!, cancellationToken);
                if (extension is null)
                {
                    return TypedResults.BadRequest(new { message = "Use a valid JPEG, PNG, or WebP profile photo." });
                }

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var filePath = Path.Combine(GetPhotoDirectory(environment), fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await using (var target = File.Create(filePath))
                {
                    await profilePhoto!.CopyToAsync(target, cancellationToken);
                }

                var protector = protectionProvider.CreateProtector("PgManagement.Api.UserAadhaar.v1");
                var user = new PgUser
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    PasswordHash = passwordHasher.HashPassword(new PgUser
                    {
                        Id = Guid.Empty,
                        Email = email,
                        PasswordHash = string.Empty
                    }, password),
                    Role = "user",
                    FullName = fullName,
                    MobileNumber = mobileNumber,
                    Address = address,
                    AadhaarProtected = aadhaarNumber.Length == 0 ? null : protector.Protect(aadhaarNumber),
                    AadhaarLastFour = aadhaarNumber.Length == 0 ? null : aadhaarNumber[^4..],
                    ProfilePhotoFileName = fileName,
                    ProfilePhotoContentType = GetImageContentType(extension)
                };

                database.Users.Add(user);
                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    File.Delete(filePath);
                    if (await database.Users.AnyAsync(account => account.Email == email, cancellationToken))
                    {
                        return TypedResults.Conflict(new { message = "An account with this email already exists." });
                    }

                    throw;
                }

                return TypedResults.Created("/api/auth/login", new RegistrationResponse(user.Email, user.FullName!, user.Role));
            })
            .AllowAnonymous()
            .WithName("RegisterUser")
            .WithSummary("Register a user")
            .WithDescription("Creates a user account with contact details and a profile photo. Aadhaar is encrypted before storage.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        app.MapGet("/api/users/me/profile-photo", async Task<IResult> (
                ClaimsPrincipal principal,
                PgDbContext database,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(principal);
                if (userId is null)
                {
                    return TypedResults.Unauthorized();
                }

                var profile = await database.Users.AsNoTracking()
                    .Where(user => user.Id == userId.Value)
                    .Select(user => new { user.ProfilePhotoFileName, user.ProfilePhotoContentType })
                    .SingleOrDefaultAsync(cancellationToken);
                if (profile?.ProfilePhotoFileName is null || profile.ProfilePhotoContentType is null)
                {
                    return TypedResults.NotFound();
                }

                var photoPath = Path.Combine(GetPhotoDirectory(environment), Path.GetFileName(profile.ProfilePhotoFileName));
                if (!File.Exists(photoPath))
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.File(File.OpenRead(photoPath), profile.ProfilePhotoContentType);
            })
            .RequireAuthorization()
            .WithName("GetMyProfilePhoto")
            .WithSummary("Get the signed-in user's profile photo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static Dictionary<string, string[]> ValidateRegistration(
        string fullName,
        string mobileNumber,
        string address,
        string email,
        string aadhaarNumber,
        string password,
        IFormFile? profilePhoto)
    {
        var errors = new Dictionary<string, string[]>();
        if (fullName.Length is < 2 or > 150) errors["fullName"] = ["Enter a name between 2 and 150 characters."];
        var mobileDigits = mobileNumber.Count(char.IsAsciiDigit);
        if (mobileNumber.Length is < 7 or > 30 || mobileDigits is < 7 or > 15 ||
            mobileNumber.Any(character => !char.IsAsciiDigit(character) && !"+()- ".Contains(character)))
            errors["mobileNumber"] = ["Enter a valid mobile number."];
        if (address.Length is < 5 or > 500) errors["address"] = ["Enter an address between 5 and 500 characters."];
        if (!new EmailAddressAttribute().IsValid(email)) errors["email"] = ["Enter a valid email address."];
        if (aadhaarNumber.Length > 0 &&
            (aadhaarNumber.Length != 12 || aadhaarNumber.Any(character => !char.IsAsciiDigit(character))))
            errors["aadhaarNumber"] = ["Enter the 12-digit Aadhaar number."];
        if (password.Length is < 12 or > 200) errors["password"] = ["Use a password between 12 and 200 characters."];
        if (profilePhoto is null || profilePhoto.Length is 0 or > MaxPhotoBytes)
            errors["profilePhoto"] = ["Choose a profile photo up to 8 MB."];

        return errors;
    }

    private static async Task<string?> GetImageExtensionAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var buffer = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0) break;
            read += count;
        }

        if (read >= 3 && buffer[0] == 0xff && buffer[1] == 0xd8 && buffer[2] == 0xff) return ".jpg";
        if (read >= 8 && buffer.AsSpan(0, 8).SequenceEqual(PngSignature)) return ".png";
        if (read >= 12 && Encoding.ASCII.GetString(buffer, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(buffer, 8, 4) == "WEBP") return ".webp";
        return null;
    }

    private static string GetImageContentType(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        _ => "image/webp"
    };

    private static string GetPhotoDirectory(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "profile-photos");

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                 principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId) ? userId : null;
    }
}