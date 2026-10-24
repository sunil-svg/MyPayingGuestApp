using System.ComponentModel.DataAnnotations;

namespace PgManagement.Api.Rooms;

/// <summary>Request data for creating a room.</summary>
public sealed record CreateRoomRequest
{
    [Required, MaxLength(30)]
    public required string RoomNumber { get; init; }

    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [Range(0, 200)]
    public int FloorNumber { get; init; }

    [Range(1, 20)]
    public int Capacity { get; init; } = 1;

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal MonthlyRent { get; init; }

    [MaxLength(1200)]
    public string Description { get; init; } = string.Empty;

    public bool IsAvailable { get; init; } = true;

    public string[] Amenities { get; init; } = [];
}

/// <summary>Request data for replacing a room's editable fields.</summary>
public sealed record UpdateRoomRequest
{
    [Required, MaxLength(30)]
    public required string RoomNumber { get; init; }

    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [Range(0, 200)]
    public int FloorNumber { get; init; }

    [Range(1, 20)]
    public int Capacity { get; init; } = 1;

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal MonthlyRent { get; init; }

    [MaxLength(1200)]
    public string Description { get; init; } = string.Empty;

    public bool IsAvailable { get; init; } = true;

    public string[] Amenities { get; init; } = [];
}

/// <summary>Room details returned to the client.</summary>
public sealed record RoomResponse(
    int Id,
    string RoomNumber,
    string Name,
    int FloorNumber,
    int Capacity,
    decimal MonthlyRent,
    string Description,
    bool IsAvailable,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<RoomPhotoResponse> Photos);

/// <summary>Room and guest details for a confirmed booking active on the dashboard date.</summary>
public sealed record OccupiedRoomResponse(
    int RoomId,
    string RoomNumber,
    string Name,
    int FloorNumber,
    int Capacity,
    decimal MonthlyRent,
    string GuestFullName,
    string GuestEmail,
    string? GuestMobileNumber,
    DateOnly CheckInDate,
    DateOnly CheckOutDate);

/// <summary>Admin dashboard counts and the rooms in each current occupancy state.</summary>
public sealed record AdminRoomOverviewResponse(
    DateOnly AsOfDate,
    int OccupiedRoomsCount,
    int AvailableRoomsCount,
    IReadOnlyList<OccupiedRoomResponse> OccupiedRooms,
    IReadOnlyList<RoomResponse> AvailableRooms);

/// <summary>Photo metadata returned with a room.</summary>
public sealed record RoomPhotoResponse(int Id, string Url, string ContentType);
public sealed record RoomPhotoFile(string FileName, string ContentType);

public enum RoomWriteStatus
{
    Saved,
    NotFound,
    DuplicateRoomNumber
}

public sealed record RoomWriteResult(RoomWriteStatus Status, RoomResponse? Room = null);

public sealed record RoomDeleteResult(IReadOnlyList<string> PhotoFileNames, bool HasBookings);