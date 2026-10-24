using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace PgManagement.Api.Bookings;

public static class BookingEndpoints
{
    public static WebApplication MapBookingEndpoints(this WebApplication app)
    {
        var bookings = app.MapGroup("/api/bookings").RequireAuthorization(policy => policy.RequireRole("user"));

        bookings.MapGet("", async Task<IResult> (
                ClaimsPrincipal principal,
                IBookingService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(principal);
                return userId is null
                    ? TypedResults.Unauthorized()
                    : TypedResults.Ok(await service.GetMineAsync(userId.Value, cancellationToken));
            })
            .WithName("GetMyBookings")
            .WithSummary("List the signed-in user's bookings")
            .WithDescription("Returns only bookings belonging to the authenticated user.")
            .Produces<IReadOnlyList<BookingResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        bookings.MapDelete("/{id:int}", async Task<IResult> (
                int id,
                ClaimsPrincipal principal,
                IBookingService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(principal);
                if (userId is null)
                {
                    return TypedResults.Unauthorized();
                }

                return await service.CancelAsync(userId.Value, id, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound();
            })
            .WithName("CancelMyBooking")
            .WithSummary("Cancel one of the signed-in user's bookings")
            .WithDescription("Cancels a confirmed booking owned by the authenticated user.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/rooms/{roomId:int}/bookings", async Task<IResult> (
                int roomId,
                CreateBookingRequest request,
                ClaimsPrincipal principal,
                IBookingService service,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(principal);
                if (userId is null)
                {
                    return TypedResults.Unauthorized();
                }

                var errors = new Dictionary<string, string[]>();
                if (request.CheckInDate == default || request.CheckInDate < DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    errors["checkInDate"] = ["Choose today or a future check-in date."];
                }

                if (request.CheckOutDate <= request.CheckInDate)
                {
                    errors["checkOutDate"] = ["Check-out must be after check-in."];
                }

                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var result = await service.CreateAsync(userId.Value, roomId, request, cancellationToken);
                return result.Status switch
                {
                    BookingCreateStatus.RoomNotFound => TypedResults.NotFound(),
                    BookingCreateStatus.RoomUnavailable => TypedResults.Conflict(
                        new { message = "This room is not currently available." }),
                    BookingCreateStatus.DateRangeUnavailable => TypedResults.Conflict(
                        new { message = "This room is already booked for part of those dates." }),
                    _ => TypedResults.Created($"/api/bookings/{result.Booking!.Id}", result.Booking)
                };
            })
            .RequireAuthorization(policy => policy.RequireRole("user"))
            .WithName("CreateRoomBooking")
            .WithSummary("Book a room")
            .WithDescription("Creates a booking if the room is available for the requested date range.")
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                 principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId) ? userId : null;
    }
}