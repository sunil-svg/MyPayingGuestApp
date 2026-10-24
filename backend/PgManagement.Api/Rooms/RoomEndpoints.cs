using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace PgManagement.Api.Rooms;

public static class RoomEndpoints
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;

    public static WebApplication MapRoomEndpoints(this WebApplication app)
    {
        var rooms = app.MapGroup("/api/rooms");

        app.MapGet("/api/admin/rooms/overview", async (
            IRoomService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAdminOverviewAsync(
                DateOnly.FromDateTime(DateTime.UtcNow),
                cancellationToken)))
            .RequireAuthorization(policy => policy.RequireRole("admin"))
            .WithName("GetAdminRoomOverview")
            .WithSummary("Get today's room occupancy overview")
            .WithDescription("Returns occupied room guest contact details to admins and available room details.")
            .Produces<AdminRoomOverviewResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        rooms.MapGet("", async (IRoomService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.GetAllAsync(cancellationToken)))
            .AllowAnonymous()
            .WithName("GetRooms")
            .WithSummary("List rooms")
            .WithDescription("Returns room details, amenities, and photo URLs for public browsing.")
            .Produces<IReadOnlyList<RoomResponse>>()
            .Produces(StatusCodes.Status401Unauthorized);

        rooms.MapGet("/{id:int}", async Task<IResult> (
                int id,
                IRoomService service,
                CancellationToken cancellationToken) =>
            {
                var room = await service.GetByIdAsync(id, cancellationToken);
                return room is null ? TypedResults.NotFound() : TypedResults.Ok(room);
            })
            .AllowAnonymous()
            .WithName("GetRoomById")
            .WithSummary("Get a room")
            .WithDescription("Returns one room with its amenities and photo URLs.")
            .Produces<RoomResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        rooms.MapGet("/{id:int}/availability", async Task<IResult> (
                int id,
                DateOnly checkInDate,
                DateOnly checkOutDate,
                IRoomService service,
                CancellationToken cancellationToken) =>
            {
                if (checkInDate == default || checkInDate < DateOnly.FromDateTime(DateTime.UtcNow) ||
                    checkOutDate <= checkInDate)
                {
                    return TypedResults.BadRequest(new { message = "Check-out must be after check-in." });
                }

                var available = await service.IsAvailableAsync(id, checkInDate, checkOutDate, cancellationToken);
                return available is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(new Bookings.RoomAvailabilityResponse(available.Value));
            })
            .AllowAnonymous()
            .WithName("CheckRoomAvailability")
            .WithSummary("Check room availability")
            .WithDescription("Checks whether a room is free during the requested date range.")
            .Produces<Bookings.RoomAvailabilityResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        var adminRooms = rooms.MapGroup("").RequireAuthorization(policy => policy.RequireRole("admin"));

        adminRooms.MapPost("", async Task<IResult> (
                CreateRoomRequest request,
                IRoomService service,
                CancellationToken cancellationToken) =>
            {
                var errors = ValidateRequest(request, request.Amenities);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var result = await service.CreateAsync(request, cancellationToken);
                if (result.Status == RoomWriteStatus.DuplicateRoomNumber)
                {
                    return TypedResults.Conflict(new { message = "That room number is already in use." });
                }

                return TypedResults.Created($"/api/rooms/{result.Room!.Id}", result.Room);
            })
            .WithName("CreateRoom")
            .WithSummary("Create a room")
            .WithDescription("Creates a room with its rent, floor, amenities, and availability.")
            .Produces<RoomResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminRooms.MapPut("/{id:int}", async Task<IResult> (
                int id,
                UpdateRoomRequest request,
                IRoomService service,
                CancellationToken cancellationToken) =>
            {
                var errors = ValidateRequest(request, request.Amenities);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var result = await service.UpdateAsync(id, request, cancellationToken);
                return result.Status switch
                {
                    RoomWriteStatus.NotFound => TypedResults.NotFound(),
                    RoomWriteStatus.DuplicateRoomNumber => TypedResults.Conflict(
                        new { message = "That room number is already in use." }),
                    _ => TypedResults.Ok(result.Room)
                };
            })
            .WithName("UpdateRoom")
            .WithSummary("Update a room")
            .WithDescription("Replaces a room's details and amenities.")
            .Produces<RoomResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminRooms.MapDelete("/{id:int}", async Task<IResult> (
                int id,
                IRoomService service,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteAsync(id, cancellationToken);
                if (result is null)
                {
                    return TypedResults.NotFound();
                }

                if (result.HasBookings)
                {
                    return TypedResults.Conflict(new { message = "Rooms with booking history cannot be deleted." });
                }

                DeletePhotoFiles(environment, result.PhotoFileNames);
                return TypedResults.NoContent();
            })
            .WithName("DeleteRoom")
            .WithSummary("Delete a room")
            .WithDescription("Deletes a room and its amenities and photos.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminRooms.MapPost("/{roomId:int}/photos", async Task<IResult> (
                int roomId,
                HttpRequest request,
                IRoomService service,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0 || file.Length > MaxPhotoBytes)
                {
                    return TypedResults.BadRequest(new { message = "Choose an image up to 8 MB." });
                }

                var extension = await GetImageExtensionAsync(file, cancellationToken);
                if (extension is null)
                {
                    return TypedResults.BadRequest(new { message = "Use a valid JPEG, PNG, or WebP image." });
                }

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var photoDirectory = GetPhotoDirectory(environment);
                Directory.CreateDirectory(photoDirectory);
                var filePath = Path.Combine(photoDirectory, fileName);

                await using (var target = File.Create(filePath))
                {
                    await file.CopyToAsync(target, cancellationToken);
                }

                var contentType = extension switch
                {
                    ".jpg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "image/webp"
                };
                var photo = await service.AddPhotoAsync(roomId, fileName, contentType, cancellationToken);
                if (photo is null)
                {
                    File.Delete(filePath);
                    return TypedResults.NotFound();
                }

                return TypedResults.Created(photo.Url, photo);
            })
            .WithName("UploadRoomPhoto")
            .WithSummary("Upload a room photo")
            .WithDescription("Uploads one JPEG, PNG, or WebP room image, up to 8 MB.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<RoomPhotoResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        adminRooms.MapDelete("/{roomId:int}/photos/{photoId:int}", async Task<IResult> (
                int roomId,
                int photoId,
                IRoomService service,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var fileName = await service.DeletePhotoAsync(roomId, photoId, cancellationToken);
                if (fileName is null)
                {
                    return TypedResults.NotFound();
                }

                DeletePhotoFiles(environment, [fileName]);
                return TypedResults.NoContent();
            })
            .WithName("DeleteRoomPhoto")
            .WithSummary("Delete a room photo")
            .WithDescription("Deletes one photo from a room.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        rooms.MapGet("/{roomId:int}/photos/{photoId:int}", async Task<IResult> (
                int roomId,
                int photoId,
                IRoomService service,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) =>
            {
                var photo = await service.GetPhotoAsync(roomId, photoId, cancellationToken);
                if (photo is null)
                {
                    return TypedResults.NotFound();
                }

                var path = Path.Combine(GetPhotoDirectory(environment), photo.FileName);
                if (!File.Exists(path))
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.File(File.OpenRead(path), photo.ContentType);
            })
            .WithName("GetRoomPhoto")
            .WithSummary("Get a room photo")
            .AllowAnonymous()
            .WithDescription("Returns an image attached to a room for public browsing.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static Dictionary<string, string[]> ValidateRequest(object request, string[]? amenities)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        if (amenities is null || amenities.Length > 20 || amenities.Any(item => item is null || item.Trim().Length > 80))
        {
            results.Add(new ValidationResult("Provide at most 20 amenities, each no longer than 80 characters.", ["amenities"]));
        }

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty("request"), (result, member) => (member, result.ErrorMessage))
            .GroupBy(item => item.member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ErrorMessage ?? "Invalid value.").ToArray());
    }

    private static async Task<string?> GetImageExtensionAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var buffer = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read >= 3 && buffer[0] == 0xff && buffer[1] == 0xd8 && buffer[2] == 0xff)
        {
            return ".jpg";
        }

        if (read >= 8 && buffer.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return ".png";
        }

        if (read >= 12 && Encoding.ASCII.GetString(buffer, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(buffer, 8, 4) == "WEBP")
        {
            return ".webp";
        }

        return null;
    }

    private static string GetPhotoDirectory(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "room-photos");

    private static void DeletePhotoFiles(IWebHostEnvironment environment, IEnumerable<string> fileNames)
    {
        var directory = GetPhotoDirectory(environment);
        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(directory, Path.GetFileName(fileName));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}