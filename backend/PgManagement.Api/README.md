# PG Management Login API

This ASP.NET Core API exposes `POST /api/auth/login`. It stores user accounts in SQL Server, verifies passwords using ASP.NET Core Identity's password hasher, and returns a signed JWT and the account role (`admin` or `user`) when credentials are valid. There is no registration endpoint; provision accounts directly in the database.

## Configuration

Install the .NET 8 SDK and make a SQL Server database available. Create the database named in the connection string if it does not already exist; the API creates the `Users` table and index, not the database. Configure these environment variables before starting the API:

```powershell
$env:ConnectionStrings__Default = "Server=SUNIL1727;Database=PgManagement;Trusted_Connection=True;TrustServerCertificate=True"
$bytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$env:Jwt__Key = [Convert]::ToBase64String($bytes)
$rng.Dispose()
```

Use a strong, unique JWT key and keep it out of source control. For production, use a managed secret store and configure SQL Server encryption and credentials according to your deployment environment.

The API creates the `Users` table and its unique email index on startup if they do not exist, including in a database that already has other tables. The configured SQL Server identity must be allowed to create the table and index. The table has `Id`, `Email`, `PasswordHash`, `Role`, `IsActive`, and `CreatedAt` columns. Existing tables are upgraded on startup with a `Role` column defaulting to `user`.

## Provision an account

Generate a password hash with the API's built-in interactive helper; password input is not echoed:

```powershell
dotnet run --project .\backend\PgManagement.Api\PgManagement.Api.csproj -- --hash-password
```

Insert the resulting hash using a parameterized SQL client/query. For example, with SQL Server Management Studio, replace the placeholders with the account email and the generated hash:

```sql
INSERT INTO dbo.Users (Id, Email, PasswordHash, Role, IsActive, CreatedAt)
VALUES (NEWID(), N'<email>', N'<generated-password-hash>', N'user', 1, SYSUTCDATETIME());
```

Use `N'admin'` instead of `N'user'` to provision an admin. Use a lowercase email address; login normalizes submitted emails to lowercase.

## Run and call the API

Start the API:

```powershell
dotnet run --project .\backend\PgManagement.Api\PgManagement.Api.csproj
```

The Angular development proxy forwards `/api` to `http://localhost:5000`. Start the API and Angular app in separate terminals. Set `$env:ASPNETCORE_URLS = "http://localhost:5000"` in the API terminal before running the command if the API does not otherwise listen on that address.

Request:

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "resident@example.com",
  "password": "your-password"
}
```

A successful response contains `accessToken`, `expiresAtUtc`, `email`, and `role`. Invalid credentials return `401 Unauthorized`; malformed login details return `400 Bad Request`. The JWT expires after 60 minutes and should be sent to APIs that require authentication as `Authorization: Bearer <accessToken>`.

Use HTTPS for the API in production so passwords and bearer tokens are not sent in cleartext.

## API settings

`Jwt__Issuer` and `Jwt__Audience` are optional environment variables; they default to `PgManagement.Api` and `PgManagement.Client`. The token validation middleware uses the same issuer, audience, and key when validating bearer tokens.

## Room management

The API creates `Rooms`, `RoomAmenities`, and `RoomPhotos` tables at startup if they do not exist. Room records include a unique room number, display name, guest capacity, floor number, monthly rent, description, and availability. Existing room rows are backfilled with the room number as their display name and a capacity of one. Amenities are stored per room. Room images (JPEG, PNG, or WebP, up to 8 MB each) are stored under `App_Data/room-photos` and served for public room browsing.

Signed-in users can list and view rooms. Only accounts with the `admin` role can create, update, or delete rooms and upload or delete photos. The Angular `/home` screen displays the catalog to both roles and shows management controls to admins.

See [Rooms.http](./Rooms.http) for request examples. Start the API on `http://localhost:5000` and the Angular app with `npm start`; its development proxy forwards `/api` calls to the API.

## Registration and bookings

Users can self-register from `/register` with a name, mobile number, address, email, password of at least 12 characters, and JPEG/PNG/WebP profile photo (up to 8 MB). Aadhaar is optional; if supplied, it must be a 12-digit number. Registration always assigns the `user` role and the client automatically signs in after account creation, then resumes the selected room/date flow. Existing accounts remain valid; new nullable profile columns are added at API startup. Aadhaar is encrypted using ASP.NET Core Data Protection before it is stored, and only its last four digits are retained separately. Profile images are stored under `App_Data/profile-photos` and served only to the signed-in account.

Room names, capacity, details, photos, and date availability are public so visitors can browse before registering. A guest's selected room and dates are carried through registration/login and rechecked after authentication. Creating/cancelling bookings requires a `user` JWT. Booking dates use a half-open range (`check-in` inclusive, `check-out` exclusive); overlapping confirmed bookings are rejected. Each booking stores the rent at booking time. Room CRUD and room-photo writes remain restricted to the `admin` role. Registration and booking request examples are in [Bookings.http](./Bookings.http).

Admins can call `GET /api/admin/rooms/overview` for today's occupied/available counts and room lists. Occupied-room entries expose only the guest's name, email, mobile number, and booking dates; this endpoint requires an `admin` JWT. The public room list never includes guest information.
