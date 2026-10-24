namespace PgManagement.Api.Bookings;

public interface IBookingService
{
    Task<IReadOnlyList<BookingResponse>> GetMineAsync(Guid userId, CancellationToken cancellationToken);

    Task<BookingCreateResult> CreateAsync(
        Guid userId,
        int roomId,
        CreateBookingRequest request,
        CancellationToken cancellationToken);

    Task<bool> CancelAsync(Guid userId, int bookingId, CancellationToken cancellationToken);
}