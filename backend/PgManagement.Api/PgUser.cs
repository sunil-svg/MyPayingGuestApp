using System.ComponentModel.DataAnnotations;

namespace PgManagement.Api;

public sealed class PgUser
{
    public Guid Id { get; set; }

    [MaxLength(256)]
    public required string Email { get; set; }

    [MaxLength(512)]
    public required string PasswordHash { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = "user";

    [MaxLength(150)]
    public string? FullName { get; set; }

    [MaxLength(30)]
    public string? MobileNumber { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(2048)]
    public string? AadhaarProtected { get; set; }

    [MaxLength(4)]
    public string? AadhaarLastFour { get; set; }

    [MaxLength(255)]
    public string? ProfilePhotoFileName { get; set; }

    [MaxLength(100)]
    public string? ProfilePhotoContentType { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<RoomBooking> Bookings { get; set; } = new List<RoomBooking>();
}
