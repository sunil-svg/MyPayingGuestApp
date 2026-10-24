using System.Data;
using Microsoft.EntityFrameworkCore;

namespace PgManagement.Api.Bookings;

public sealed class BookingService(PgDbContext database) : IBookingService
{
    public async Task<IReadOnlyList<BookingResponse>> GetMineAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var bookings = await database.RoomBookings.AsNoTracking()
            .Where(booking => booking.UserId == userId)
            .Include(booking => booking.Room)
            .OrderByDescending(booking => booking.CreatedAt)
            .ToArrayAsync(cancellationToken);

        return bookings.Select(ToResponse).ToArray();
    }

    public async Task<BookingCreateResult> CreateAsync(
        Guid userId,
        int roomId,
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var room = await database.Rooms.SingleOrDefaultAsync(item => item.Id == roomId, cancellationToken);
        if (room is null)
        {
            return new BookingCreateResult(BookingCreateStatus.RoomNotFound);
        }

        if (!room.IsAvailable)
        {
            return new BookingCreateResult(BookingCreateStatus.RoomUnavailable);
        }

        var hasOverlap = await database.RoomBookings.AnyAsync(booking =>
            booking.RoomId == roomId &&
            booking.Status == "confirmed" &&
            booking.CheckInDate < request.CheckOutDate &&
            booking.CheckOutDate > request.CheckInDate,
            cancellationToken);
        if (hasOverlap)
        {
            return new BookingCreateResult(BookingCreateStatus.DateRangeUnavailable);
        }

        var booking = new RoomBooking
        {
            RoomId = room.Id,
            UserId = userId,
            CheckInDate = request.CheckInDate,
            CheckOutDate = request.CheckOutDate,
            MonthlyRentAtBooking = room.MonthlyRent,
            Status = "confirmed"
        };

        database.RoomBookings.Add(booking);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BookingCreateResult(
            BookingCreateStatus.Created,
            new BookingResponse(
                booking.Id,
                room.Id,
                room.RoomNumber,
                booking.CheckInDate,
                booking.CheckOutDate,
                booking.MonthlyRentAtBooking,
                booking.Status,
                booking.CreatedAt));
    }

    public async Task<bool> CancelAsync(Guid userId, int bookingId, CancellationToken cancellationToken)
    {
        var booking = await database.RoomBookings.SingleOrDefaultAsync(
            item => item.Id == bookingId && item.UserId == userId && item.Status == "confirmed",
            cancellationToken);
        if (booking is null)
        {
            return false;
        }

        booking.Status = "cancelled";
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BookingResponse ToResponse(RoomBooking booking) => new(
        booking.Id,
        booking.RoomId,
        booking.Room.RoomNumber,
        booking.CheckInDate,
        booking.CheckOutDate,
        booking.MonthlyRentAtBooking,
        booking.Status,
        booking.CreatedAt);
}