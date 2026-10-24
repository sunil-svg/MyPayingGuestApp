namespace PgManagement.Api;

public sealed class RoomBooking
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public Guid UserId { get; set; }

    public DateOnly CheckInDate { get; set; }

    public DateOnly CheckOutDate { get; set; }

    public decimal MonthlyRentAtBooking { get; set; }

    public string Status { get; set; } = "confirmed";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Room Room { get; set; } = null!;

    public PgUser User { get; set; } = null!;
}