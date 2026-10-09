using System.Text.Json;
using Hotel.Application.Abstractions;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using Hotel.Domain.Rooms;
using Hotel.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hotel.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RatePlan> RatePlans => Set<RatePlan>();
    public DbSet<DailyRate> DailyRates => Set<DailyRate>();
    public DbSet<InventoryOverride> InventoryOverrides => Set<InventoryOverride>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();
    public DbSet<ChannelMapping> ChannelMappings => Set<ChannelMapping>();
    public DbSet<ChannelSyncLog> ChannelSyncLogs => Set<ChannelSyncLog>();
    public DbSet<AriOutboxMessage> AriOutbox => Set<AriOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        var isNpgsql = Database.IsNpgsql();

        b.Entity<RoomType>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.BasePrice).HasPrecision(12, 2);
            e.HasMany(x => x.Rooms).WithOne(x => x.RoomType).HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.RatePlans).WithOne(x => x.RoomType).HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Room>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(16);
        });

        b.Entity<RatePlan>(e =>
        {
            e.HasIndex(x => new { x.RoomTypeId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PriceFactor).HasPrecision(6, 4);
        });

        b.Entity<DailyRate>(e =>
        {
            e.HasKey(x => new { x.RatePlanId, x.Date });
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.HasOne(x => x.RatePlan).WithMany().HasForeignKey(x => x.RatePlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InventoryOverride>(e =>
        {
            e.HasKey(x => new { x.RoomTypeId, x.Date });
            e.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.ChannelConnectionId, x.ExternalReference }).IsUnique();
            e.HasIndex(x => new { x.RoomTypeId, x.CheckIn, x.CheckOut });
            e.HasIndex(x => x.Status);
            e.Property(x => x.Reference).HasMaxLength(16);
            e.Property(x => x.ExternalReference).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.Property(x => x.CommissionAmount).HasPrecision(12, 2);
            e.OwnsOne(x => x.Guest, g =>
            {
                g.Property(p => p.FirstName).HasMaxLength(100).HasColumnName("GuestFirstName");
                g.Property(p => p.LastName).HasMaxLength(100).HasColumnName("GuestLastName");
                g.Property(p => p.Email).HasMaxLength(254).HasColumnName("GuestEmail");
                g.Property(p => p.Phone).HasMaxLength(40).HasColumnName("GuestPhone");
                g.Property(p => p.Country).HasMaxLength(2).HasColumnName("GuestCountry");
                g.HasIndex(p => p.Email);
            });
            e.Navigation(x => x.Guest).IsRequired();
            e.HasOne(x => x.RoomType).WithMany().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RatePlan).WithMany().HasForeignKey(x => x.RatePlanId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ChannelConnection>().WithMany().HasForeignKey(x => x.ChannelConnectionId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.Stay);
            if (isNpgsql) e.Property(x => x.Version).IsRowVersion(); // maps to PostgreSQL xmin
            else e.Ignore(x => x.Version);
        });

        b.Entity<ChannelConnection>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.CommissionPercent).HasPrecision(5, 2);
            e.Property(x => x.RateMarkupPercent).HasPrecision(5, 2);
            e.Property(x => x.Settings).HasConversion(
                new ValueConverter<Dictionary<string, string>, string>(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new()),
                new ValueComparer<Dictionary<string, string>>(
                    (a, c) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(c, JsonSerializerOptions.Default),
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
                    v => new Dictionary<string, string>(v)));
            if (isNpgsql) e.Property(x => x.Settings).HasColumnType("jsonb");
            e.HasMany(x => x.Mappings).WithOne().HasForeignKey(x => x.ChannelConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChannelMapping>(e =>
        {
            e.HasIndex(x => new { x.ChannelConnectionId, x.RoomTypeId, x.RatePlanId }).IsUnique();
            e.Property(x => x.ExternalRoomId).HasMaxLength(100);
            e.Property(x => x.ExternalRateId).HasMaxLength(100);
            e.Property(x => x.ImportUrl).HasMaxLength(2000);
            e.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<RatePlan>().WithMany().HasForeignKey(x => x.RatePlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChannelSyncLog>(e =>
        {
            e.HasIndex(x => new { x.ChannelConnectionId, x.CreatedAt });
            e.HasOne<ChannelConnection>().WithMany().HasForeignKey(x => x.ChannelConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AriOutboxMessage>(e => e.HasIndex(x => x.ProcessedAt));

        if (!isNpgsql) ApplySqliteCompatibility(b);
    }

    /// <summary>SQLite (used by tests) cannot order/compare DateTimeOffset; store as ticks.</summary>
    private static void ApplySqliteCompatibility(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTimeOffset) || prop.ClrType == typeof(DateTimeOffset?))
                prop.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }
}
