using System.ComponentModel.DataAnnotations;

namespace PgManagement.Api.Bookings;

/// <summary>Request to reserve a room for a date range.</summary>
public sealed record CreateBookingRequest
{
    [Required]
    public DateOnly CheckInDate { get; init; }

    [Required]
    public DateOnly CheckOutDate { get; init; }
}

/// <summary>Booking information visible to its owner.</summary>
public sealed record BookingResponse(
    int Id,
    int RoomId,
    string RoomNumber,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    decimal MonthlyRentAtBooking,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Availability of a room for the requested date range.</summary>
public sealed record RoomAvailabilityResponse(bool IsAvailable);

public enum BookingCreateStatus
{
    Created,
    RoomNotFound,
    RoomUnavailable,
    DateRangeUnavailable
}

public sealed record BookingCreateResult(BookingCreateStatus Status, BookingResponse? Booking = null);