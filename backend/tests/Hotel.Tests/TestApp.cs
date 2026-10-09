using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hotel.Application.Channels;
using Hotel.Domain.Channels;
using Hotel.Domain.Rooms;
using Hotel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hotel.Tests;

/// <summary>API host on an in-memory SQLite database with a fake channel connector.</summary>
public class TestApp : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin12345!";

    // File based so concurrent requests get their own connections (a shared in-memory connection is not thread-safe).
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hotel-tests-{Guid.NewGuid():N}.db");
    public FakeConnector Fake { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("ChannelSync:Enabled", "false");
        builder.UseSetting("Database:SeedSampleData", "false");
        builder.UseSetting("RateLimiting:PublicPermitPerMinute", "10000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=30"));

            services.RemoveAll<IChannelConnector>();
            services.AddSingleton<IChannelConnector>(Fake);
        });
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    public async Task<HttpClient> AdminClientAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates a room type with <paramref name="rooms"/> rooms and one rate plan.</summary>
    public async Task<(RoomType Type, RatePlan Plan)> SeedRoomTypeAsync(int rooms, decimal price = 1000m, string? code = null)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        code ??= "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var type = new RoomType { Code = code, Name = $"Type {code}", BasePrice = price, MaxAdults = 2, MaxChildren = 1, Currency = "TRY" };
        var plan = new RatePlan { Code = "BAR", Name = "Best available", RoomTypeId = type.Id };
        type.RatePlans.Add(plan);
        db.RoomTypes.Add(type);
        for (var i = 0; i < rooms; i++) db.Rooms.Add(new Room { Number = $"{code}-{i}", RoomTypeId = type.Id });
        await db.SaveChangesAsync();
        return (type, plan);
    }

    public async Task<ChannelConnection> SeedChannelAsync(Guid roomTypeId, Guid ratePlanId, decimal markup = 0, decimal commission = 0)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = new ChannelConnection
        {
            Name = "Fake OTA", Channel = FakeConnector.FakeType, IsEnabled = true, ExternalPropertyId = "P1",
            RateMarkupPercent = markup, CommissionPercent = commission,
        };
        conn.Mappings.Add(new ChannelMapping { RoomTypeId = roomTypeId, ExternalRoomId = "R1" });
        conn.Mappings.Add(new ChannelMapping { RoomTypeId = roomTypeId, RatePlanId = ratePlanId, ExternalRoomId = "R1", ExternalRateId = "RATE1" });
        db.ChannelConnections.Add(conn);
        await db.SaveChangesAsync();
        return conn;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }

    private record TokenResponse(string AccessToken, string RefreshToken);
}
