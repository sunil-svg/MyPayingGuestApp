namespace PgManagement.Api.Rooms;

public interface IRoomService
{
    Task<IReadOnlyList<RoomResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<RoomResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<bool?> IsAvailableAsync(
        int id,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        CancellationToken cancellationToken);

    Task<AdminRoomOverviewResponse> GetAdminOverviewAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken);

    Task<RoomWriteResult> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken);

    Task<RoomWriteResult> UpdateAsync(int id, UpdateRoomRequest request, CancellationToken cancellationToken);

    Task<RoomDeleteResult?> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<RoomPhotoResponse?> AddPhotoAsync(
        int roomId,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);

    Task<string?> DeletePhotoAsync(int roomId, int photoId, CancellationToken cancellationToken);
        Task<RoomPhotoFile?> GetPhotoAsync(int roomId, int photoId, CancellationToken cancellationToken);
}