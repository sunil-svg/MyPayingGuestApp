using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PgManagement.Api;
using PgManagement.Api.Bookings;
using PgManagement.Api.Rooms;
using PgManagement.Api.Users;

if (args.Length == 1 && args[0] == "--hash-password")
{
    Console.Write("Enter password: ");
    var password = ReadHiddenPassword();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("Password must not be empty.");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine(new PasswordHasher<PgUser>().HashPassword(
        new PgUser { Email = "provisioning", PasswordHash = string.Empty },
        password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Configure the SQL Server connection with ConnectionStrings__Default.");
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Configure the JWT signing key with Jwt__Key.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt__Key must be at least 32 UTF-8 bytes.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PgManagement.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PgManagement.Client";

builder.Services.AddDbContext<PgDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<PasswordHasher<PgUser>>();
builder.Services.AddDataProtection();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services
    .AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<PgDbContext>();
    await database.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Users (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
                Email nvarchar(256) NOT NULL,
                PasswordHash nvarchar(512) NOT NULL,
                Role nvarchar(20) NOT NULL CONSTRAINT DF_Users_Role DEFAULT N'user',
                FullName nvarchar(150) NULL,
                MobileNumber nvarchar(30) NULL,
                Address nvarchar(500) NULL,
                AadhaarProtected nvarchar(2048) NULL,
                AadhaarLastFour nvarchar(4) NULL,
                ProfilePhotoFileName nvarchar(255) NULL,
                ProfilePhotoContentType nvarchar(100) NULL,
                IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
                CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_Users_CreatedAt
                    DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
                CONSTRAINT CK_Users_Role CHECK (Role IN (N'admin', N'user'))
            );
        END;

        IF COL_LENGTH(N'dbo.Users', N'Role') IS NULL
        BEGIN
            ALTER TABLE dbo.Users
                ADD Role nvarchar(20) NOT NULL
                    CONSTRAINT DF_Users_Role DEFAULT N'user';
        END;

        IF COL_LENGTH(N'dbo.Users', N'FullName') IS NULL
            ALTER TABLE dbo.Users ADD FullName nvarchar(150) NULL;
        IF COL_LENGTH(N'dbo.Users', N'MobileNumber') IS NULL
            ALTER TABLE dbo.Users ADD MobileNumber nvarchar(30) NULL;
        IF COL_LENGTH(N'dbo.Users', N'Address') IS NULL
            ALTER TABLE dbo.Users ADD Address nvarchar(500) NULL;
        IF COL_LENGTH(N'dbo.Users', N'AadhaarProtected') IS NULL
            ALTER TABLE dbo.Users ADD AadhaarProtected nvarchar(2048) NULL;
        IF COL_LENGTH(N'dbo.Users', N'AadhaarLastFour') IS NULL
            ALTER TABLE dbo.Users ADD AadhaarLastFour nvarchar(4) NULL;
        IF COL_LENGTH(N'dbo.Users', N'ProfilePhotoFileName') IS NULL
            ALTER TABLE dbo.Users ADD ProfilePhotoFileName nvarchar(255) NULL;
        IF COL_LENGTH(N'dbo.Users', N'ProfilePhotoContentType') IS NULL
            ALTER TABLE dbo.Users ADD ProfilePhotoContentType nvarchar(100) NULL;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = N'CK_Users_Role'
                AND parent_object_id = OBJECT_ID(N'dbo.Users')
        )
        BEGIN
            ALTER TABLE dbo.Users
                ADD CONSTRAINT CK_Users_Role CHECK (Role IN (N'admin', N'user'));
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_Users_Email'
                AND object_id = OBJECT_ID(N'dbo.Users')
        )
        BEGIN
            CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email);
        END;

        IF OBJECT_ID(N'dbo.Rooms', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Rooms (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rooms PRIMARY KEY,
                RoomNumber nvarchar(30) NOT NULL,
                Name nvarchar(100) NOT NULL CONSTRAINT DF_Rooms_Name DEFAULT N'',
                FloorNumber int NOT NULL,
                Capacity int NOT NULL CONSTRAINT DF_Rooms_Capacity DEFAULT 1,
                MonthlyRent decimal(10,2) NOT NULL,
                Description nvarchar(1200) NOT NULL CONSTRAINT DF_Rooms_Description DEFAULT N'',
                IsAvailable bit NOT NULL CONSTRAINT DF_Rooms_IsAvailable DEFAULT 1,
                CONSTRAINT CK_Rooms_FloorNumber CHECK (FloorNumber BETWEEN 0 AND 200),
                CONSTRAINT CK_Rooms_Capacity CHECK (Capacity BETWEEN 1 AND 20),
                CONSTRAINT CK_Rooms_MonthlyRent CHECK (MonthlyRent > 0)
            );
        END;

        IF COL_LENGTH(N'dbo.Rooms', N'Name') IS NULL
            ALTER TABLE dbo.Rooms ADD Name nvarchar(100) NOT NULL CONSTRAINT DF_Rooms_Name DEFAULT N'';
        IF COL_LENGTH(N'dbo.Rooms', N'Capacity') IS NULL
            ALTER TABLE dbo.Rooms ADD Capacity int NOT NULL CONSTRAINT DF_Rooms_Capacity DEFAULT 1;

        EXEC(N'UPDATE dbo.Rooms SET Name = RoomNumber WHERE LEN(Name) = 0');

        IF NOT EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE name = N'CK_Rooms_Capacity' AND parent_object_id = OBJECT_ID(N'dbo.Rooms')
        )
        BEGIN
            EXEC(N'ALTER TABLE dbo.Rooms ADD CONSTRAINT CK_Rooms_Capacity CHECK (Capacity BETWEEN 1 AND 20)');
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_Rooms_RoomNumber' AND object_id = OBJECT_ID(N'dbo.Rooms')
        )
        BEGIN
            CREATE UNIQUE INDEX IX_Rooms_RoomNumber ON dbo.Rooms (RoomNumber);
        END;

        IF OBJECT_ID(N'dbo.RoomAmenities', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RoomAmenities (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoomAmenities PRIMARY KEY,
                RoomId int NOT NULL,
                Name nvarchar(80) NOT NULL,
                CONSTRAINT FK_RoomAmenities_Rooms FOREIGN KEY (RoomId)
                    REFERENCES dbo.Rooms (Id) ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_RoomAmenities_RoomId_Name' AND object_id = OBJECT_ID(N'dbo.RoomAmenities')
        )
        BEGIN
            CREATE UNIQUE INDEX IX_RoomAmenities_RoomId_Name
                ON dbo.RoomAmenities (RoomId, Name);
        END;

        IF OBJECT_ID(N'dbo.RoomPhotos', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RoomPhotos (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoomPhotos PRIMARY KEY,
                RoomId int NOT NULL,
                FileName nvarchar(255) NOT NULL,
                ContentType nvarchar(100) NOT NULL,
                CONSTRAINT FK_RoomPhotos_Rooms FOREIGN KEY (RoomId)
                    REFERENCES dbo.Rooms (Id) ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_RoomPhotos_FileName' AND object_id = OBJECT_ID(N'dbo.RoomPhotos')
        )
        BEGIN
            CREATE UNIQUE INDEX IX_RoomPhotos_FileName ON dbo.RoomPhotos (FileName);
        END;

        IF OBJECT_ID(N'dbo.RoomBookings', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RoomBookings (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoomBookings PRIMARY KEY,
                RoomId int NOT NULL,
                UserId uniqueidentifier NOT NULL,
                CheckInDate date NOT NULL,
                CheckOutDate date NOT NULL,
                MonthlyRentAtBooking decimal(10,2) NOT NULL,
                Status nvarchar(20) NOT NULL CONSTRAINT DF_RoomBookings_Status DEFAULT N'confirmed',
                CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_RoomBookings_CreatedAt
                    DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
                CONSTRAINT CK_RoomBookings_Dates CHECK (CheckOutDate > CheckInDate),
                CONSTRAINT FK_RoomBookings_Rooms FOREIGN KEY (RoomId)
                    REFERENCES dbo.Rooms (Id) ON DELETE NO ACTION,
                CONSTRAINT FK_RoomBookings_Users FOREIGN KEY (UserId)
                    REFERENCES dbo.Users (Id) ON DELETE NO ACTION
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_RoomBookings_Room_Dates' AND object_id = OBJECT_ID(N'dbo.RoomBookings')
        )
        BEGIN
            CREATE INDEX IX_RoomBookings_Room_Dates
                ON dbo.RoomBookings (RoomId, CheckInDate, CheckOutDate);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_RoomBookings_User_CreatedAt' AND object_id = OBJECT_ID(N'dbo.RoomBookings')
        )
        BEGIN
            CREATE INDEX IX_RoomBookings_User_CreatedAt
                ON dbo.RoomBookings (UserId, CreatedAt DESC);
        END;
        """);
}

app.UseAuthentication();
app.UseAuthorization();
app.MapRoomEndpoints();
app.MapUserEndpoints();
app.MapBookingEndpoints();

app.MapPost("/api/auth/login", async Task<IResult> (
    LoginRequest request,
    PgDbContext database,
    PasswordHasher<PgUser> passwordHasher) =>
{
    var suppliedEmail = request.Email;
    var password = request.Password;
    if (string.IsNullOrWhiteSpace(suppliedEmail) ||
        !new EmailAddressAttribute().IsValid(suppliedEmail.Trim()) ||
        string.IsNullOrWhiteSpace(password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["email"] = new[] { "A valid email and a non-empty password are required." }
        });
    }

    var email = suppliedEmail.Trim().ToLowerInvariant();
    var user = await database.Users.SingleOrDefaultAsync(candidate => candidate.Email == email);
    if (user is null || !user.IsActive || (user.Role != "admin" && user.Role != "user"))
    {
        return Results.Unauthorized();
    }

    PasswordVerificationResult verification;
    try
    {
        verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
    }
    catch (FormatException)
    {
        return Results.Unauthorized();
    }
    if (verification == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    if (verification == PasswordVerificationResult.SuccessRehashNeeded)
    {
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        await database.SaveChangesAsync();
    }

    var expiresAt = DateTime.UtcNow.AddMinutes(60);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        },
        expires: expiresAt,
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            SecurityAlgorithms.HmacSha256));

    return Results.Ok(new LoginResponse(
        new JwtSecurityTokenHandler().WriteToken(token),
        expiresAt,
        user.Email,
        user.Role));
});

app.Run();

static string ReadHiddenPassword()
{
    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            return password.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string Email, string Role);
