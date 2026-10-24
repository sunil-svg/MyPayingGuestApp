using Microsoft.EntityFrameworkCore;

namespace PgManagement.Api;

public sealed class PgDbContext(DbContextOptions<PgDbContext> options) : DbContext(options)
{
    public DbSet<PgUser> Users => Set<PgUser>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomAmenity> RoomAmenities => Set<RoomAmenity>();
    public DbSet<RoomPhoto> RoomPhotos => Set<RoomPhoto>();
    public DbSet<RoomBooking> RoomBookings => Set<RoomBooking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PgUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(20).HasDefaultValue("user").IsRequired();
            entity.Property(user => user.FullName).HasMaxLength(150);
            entity.Property(user => user.MobileNumber).HasMaxLength(30);
            entity.Property(user => user.Address).HasMaxLength(500);
            entity.Property(user => user.AadhaarProtected).HasMaxLength(2048);
            entity.Property(user => user.AadhaarLastFour).HasMaxLength(4);
            entity.Property(user => user.ProfilePhotoFileName).HasMaxLength(255);
            entity.Property(user => user.ProfilePhotoContentType).HasMaxLength(100);
            entity.HasMany(user => user.Bookings).WithOne(booking => booking.User)
                .HasForeignKey(booking => booking.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(room => room.Id);
            entity.HasIndex(room => room.RoomNumber).IsUnique();
            entity.Property(room => room.RoomNumber).HasMaxLength(30).IsRequired();
            entity.Property(room => room.Name).HasMaxLength(100).IsRequired();
            entity.Property(room => room.Capacity).HasDefaultValue(1);
            entity.Property(room => room.MonthlyRent).HasPrecision(10, 2);
            entity.Property(room => room.Description).HasMaxLength(1200).IsRequired();
            entity.HasMany(room => room.Amenities).WithOne(amenity => amenity.Room)
                .HasForeignKey(amenity => amenity.RoomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(room => room.Photos).WithOne(photo => photo.Room)
                .HasForeignKey(photo => photo.RoomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(room => room.Bookings).WithOne(booking => booking.Room)
                .HasForeignKey(booking => booking.RoomId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoomAmenity>(entity =>
        {
            entity.HasKey(amenity => amenity.Id);
            entity.HasIndex(amenity => new { amenity.RoomId, amenity.Name }).IsUnique();
            entity.Property(amenity => amenity.Name).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<RoomPhoto>(entity =>
        {
            entity.HasKey(photo => photo.Id);
            entity.Property(photo => photo.FileName).HasMaxLength(255).IsRequired();
            entity.Property(photo => photo.ContentType).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<RoomBooking>(entity =>
        {
            entity.HasKey(booking => booking.Id);
            entity.HasIndex(booking => new { booking.RoomId, booking.CheckInDate, booking.CheckOutDate });
            entity.HasIndex(booking => new { booking.UserId, booking.CreatedAt });
            entity.Property(booking => booking.MonthlyRentAtBooking).HasPrecision(10, 2);
            entity.Property(booking => booking.Status).HasMaxLength(20).IsRequired();
        });
    }
}
