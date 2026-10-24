using Microsoft.EntityFrameworkCore;
using System.Data;

namespace PgManagement.Api.Rooms;

public sealed class RoomService(PgDbContext database) : IRoomService
{
    public async Task<IReadOnlyList<RoomResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rooms = await database.Rooms.AsNoTracking()
            .Include(room => room.Amenities)
            .Include(room => room.Photos)
            .OrderBy(room => room.FloorNumber)
            .ThenBy(room => room.RoomNumber)
            .ToListAsync(cancellationToken);

        return rooms.Select(ToResponse).ToArray();
    }

    public async Task<RoomResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var room = await database.Rooms.AsNoTracking()
            .Include(item => item.Amenities)
            .Include(item => item.Photos)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return room is null ? null : ToResponse(room);
    }

    public async Task<bool?> IsAvailableAsync(
        int id,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        CancellationToken cancellationToken)
    {
        var room = await database.Rooms.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.IsAvailable })
            .SingleOrDefaultAsync(cancellationToken);
        if (room is null)
        {
            return null;
        }

        if (!room.IsAvailable)
        {
            return false;
        }

        return !await database.RoomBookings.AnyAsync(booking =>
            booking.RoomId == id &&
            booking.Status == "confirmed" &&
            booking.CheckInDate < checkOutDate &&
            booking.CheckOutDate > checkInDate,
            cancellationToken);
    }

    public async Task<AdminRoomOverviewResponse> GetAdminOverviewAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var activeBookings = await database.RoomBookings.AsNoTracking()
            .Where(booking => booking.Status == "confirmed" &&
                booking.CheckInDate <= asOfDate && booking.CheckOutDate > asOfDate)
            .Include(booking => booking.Room)
            .Include(booking => booking.User)
            .OrderBy(booking => booking.Room.FloorNumber)
            .ThenBy(booking => booking.Room.RoomNumber)
            .Select(booking => new OccupiedRoomResponse(
                booking.RoomId,
                booking.Room.RoomNumber,
                booking.Room.Name,
                booking.Room.FloorNumber,
                booking.Room.Capacity,
                booking.Room.MonthlyRent,
                booking.User.FullName ?? booking.User.Email,
                booking.User.Email,
                booking.User.MobileNumber,
                booking.CheckInDate,
                booking.CheckOutDate))
            .ToListAsync(cancellationToken);

        var occupiedRooms = activeBookings
            .DistinctBy(booking => booking.RoomId)
            .ToArray();

        var availableRoomEntities = await database.Rooms.AsNoTracking()
            .Include(room => room.Amenities)
            .Include(room => room.Photos)
            .Where(room => room.IsAvailable && !database.RoomBookings.Any(booking =>
                booking.RoomId == room.Id &&
                booking.Status == "confirmed" &&
                booking.CheckInDate <= asOfDate &&
                booking.CheckOutDate > asOfDate))
            .OrderBy(room => room.FloorNumber)
            .ThenBy(room => room.RoomNumber)
            .ToListAsync(cancellationToken);

        return new AdminRoomOverviewResponse(
            asOfDate,
            occupiedRooms.Length,
            availableRoomEntities.Count,
            occupiedRooms,
            availableRoomEntities.Select(ToResponse).ToArray());
    }

    public async Task<RoomWriteResult> CreateAsync(
        CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var roomNumber = request.RoomNumber.Trim();
        if (await database.Rooms.AnyAsync(room => room.RoomNumber == roomNumber, cancellationToken))
        {
            return new RoomWriteResult(RoomWriteStatus.DuplicateRoomNumber);
        }

        var room = new Room
        {
            RoomNumber = roomNumber,
            Name = request.Name.Trim(),
            FloorNumber = request.FloorNumber,
            Capacity = request.Capacity,
            MonthlyRent = request.MonthlyRent,
            Description = request.Description?.Trim() ?? string.Empty,
            IsAvailable = request.IsAvailable,
            Amenities = ToAmenities(request.Amenities)
        };

        database.Rooms.Add(room);
        await database.SaveChangesAsync(cancellationToken);
        return new RoomWriteResult(RoomWriteStatus.Saved, ToResponse(room));
    }

    public async Task<RoomWriteResult> UpdateAsync(
        int id,
        UpdateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var room = await database.Rooms
            .Include(item => item.Amenities)
            .Include(item => item.Photos)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (room is null)
        {
            return new RoomWriteResult(RoomWriteStatus.NotFound);
        }

        var roomNumber = request.RoomNumber.Trim();
        if (await database.Rooms.AnyAsync(
            item => item.Id != id && item.RoomNumber == roomNumber,
            cancellationToken))
        {
            return new RoomWriteResult(RoomWriteStatus.DuplicateRoomNumber);
        }

        room.RoomNumber = roomNumber;
        room.Name = request.Name.Trim();
        room.FloorNumber = request.FloorNumber;
        room.Capacity = request.Capacity;
        room.MonthlyRent = request.MonthlyRent;
        room.Description = request.Description?.Trim() ?? string.Empty;
        room.IsAvailable = request.IsAvailable;
        database.RoomAmenities.RemoveRange(room.Amenities);
        room.Amenities = ToAmenities(request.Amenities);

        await database.SaveChangesAsync(cancellationToken);
        return new RoomWriteResult(RoomWriteStatus.Saved, ToResponse(room));
    }

    public async Task<RoomDeleteResult?> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var room = await database.Rooms
            .Include(item => item.Photos)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (room is null)
        {
            return null;
        }

        if (await database.RoomBookings.AnyAsync(booking => booking.RoomId == id, cancellationToken))
        {
            return new RoomDeleteResult([], true);
        }

        var result = new RoomDeleteResult(room.Photos.Select(photo => photo.FileName).ToArray(), false);
        database.Rooms.Remove(room);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<RoomPhotoResponse?> AddPhotoAsync(
        int roomId,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var room = await database.Rooms.SingleOrDefaultAsync(item => item.Id == roomId, cancellationToken);
        if (room is null)
        {
            return null;
        }

        var photo = new RoomPhoto
        {
            RoomId = roomId,
            FileName = fileName,
            ContentType = contentType
        };
        database.RoomPhotos.Add(photo);
        await database.SaveChangesAsync(cancellationToken);
        return new RoomPhotoResponse(
            photo.Id,
            $"/api/rooms/{roomId}/photos/{photo.Id}",
            photo.ContentType);
    }

    public async Task<string?> DeletePhotoAsync(int roomId, int photoId, CancellationToken cancellationToken)
    {
        var photo = await database.RoomPhotos.SingleOrDefaultAsync(
            item => item.RoomId == roomId && item.Id == photoId,
            cancellationToken);
        if (photo is null)
        {
            return null;
        }

        database.RoomPhotos.Remove(photo);
        await database.SaveChangesAsync(cancellationToken);
        return photo.FileName;
    }

    public async Task<RoomPhotoFile?> GetPhotoAsync(
        int roomId,
        int photoId,
        CancellationToken cancellationToken)
    {
        return await database.RoomPhotos.AsNoTracking()
            .Where(photo => photo.RoomId == roomId && photo.Id == photoId)
            .Select(photo => new RoomPhotoFile(photo.FileName, photo.ContentType))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static ICollection<RoomAmenity> ToAmenities(IEnumerable<string> amenities) => amenities
        .Select(name => name.Trim())
        .Where(name => name.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(name => new RoomAmenity { Name = name })
        .ToList();

    private static RoomResponse ToResponse(Room room) => new(
        room.Id,
        room.RoomNumber,
        room.Name,
        room.FloorNumber,
        room.Capacity,
        room.MonthlyRent,
        room.Description,
        room.IsAvailable,
        room.Amenities.Select(amenity => amenity.Name).Order().ToArray(),
        room.Photos
            .OrderBy(photo => photo.Id)
            .Select(photo => new RoomPhotoResponse(
                photo.Id,
                $"/api/rooms/{room.Id}/photos/{photo.Id}",
                photo.ContentType))
            .ToArray());
}