using System.ComponentModel.DataAnnotations;

namespace PgManagement.Api;

public sealed class Room
{
    public int Id { get; set; }

    [MaxLength(30)]
    public required string RoomNumber { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int FloorNumber { get; set; }

    public int Capacity { get; set; } = 1;

    public decimal MonthlyRent { get; set; }

    [MaxLength(1200)]
    public string Description { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;

    public ICollection<RoomAmenity> Amenities { get; set; } = new List<RoomAmenity>();

    public ICollection<RoomPhoto> Photos { get; set; } = new List<RoomPhoto>();

    public ICollection<RoomBooking> Bookings { get; set; } = new List<RoomBooking>();
}

public sealed class RoomAmenity
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    [MaxLength(80)]
    public required string Name { get; set; }

    public Room Room { get; set; } = null!;
}

public sealed class RoomPhoto
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    [MaxLength(255)]
    public required string FileName { get; set; }

    [MaxLength(100)]
    public required string ContentType { get; set; }

    public Room Room { get; set; } = null!;
}