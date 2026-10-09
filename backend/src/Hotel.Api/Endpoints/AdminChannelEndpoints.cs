using Hotel.Api.Infrastructure;
using Hotel.Application.Abstractions;
using Hotel.Application.Channels;
using Hotel.Application.Common;
using Hotel.Domain.Channels;
using Hotel.Domain.Common;
using Hotel.Infrastructure.Channels.ICal;
using Hotel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hotel.Api.Endpoints;

public static class AdminChannelEndpoints
{
    public static void MapAdminChannelEndpoints(this RouteGroupBuilder admin)
    {
        var g = admin.MapGroup("/channels").WithTags("Admin: Channels");

        g.MapGet("/providers", (ChannelSyncService sync) => sync.Describe());

        g.MapGet("/", async (AppDbContext db, HttpRequest http, IOptions<HotelOptions> hotel, CancellationToken ct) =>
        {
            var list = await db.ChannelConnections.AsNoTracking().Include(c => c.Mappings).OrderBy(c => c.Name).ToListAsync(ct);
            return list.Select(c => ToDto(c, http, hotel.Value));
        });

        g.MapGet("/{id:guid}", async Task<Results<Ok<ChannelDto>, NotFound>> (Guid id, AppDbContext db, HttpRequest http, IOptions<HotelOptions> hotel, CancellationToken ct) =>
        {
            var c = await db.ChannelConnections.AsNoTracking().Include(x => x.Mappings).FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(c, http, hotel.Value));
        });

        g.MapPost("/", async (ChannelInput input, AppDbContext db, ISecretProtector secrets, CancellationToken ct) =>
        {
            var c = new ChannelConnection { Name = "", Channel = input.Channel };
            input.Apply(c, secrets);
            if (c.Channel == ChannelType.ICal) c.Settings[ICalConnector.ExportTokenSetting] = ReferenceGenerator.NewToken();
            db.ChannelConnections.Add(c);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/admin/channels/{c.Id}", new { c.Id });
        });

        g.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, ChannelInput input, AppDbContext db, ISecretProtector secrets, CancellationToken ct) =>
        {
            var c = await db.ChannelConnections.FindAsync([id], ct);
            if (c is null) return TypedResults.NotFound();
            if (input.Channel != c.Channel) throw new DomainException("invalid_input", "Channel type cannot be changed.");
            input.Apply(c, secrets);
            c.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        g.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.ChannelConnections.FindAsync([id], ct);
            if (c is null) return TypedResults.NotFound();
            db.ChannelConnections.Remove(c);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // Replaces all mappings of the connection.
        g.MapPut("/{id:guid}/mappings", async (Guid id, List<MappingInput> input, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.ChannelConnections.Include(x => x.Mappings).FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Channel not found.");
            if (input.Any(m => string.IsNullOrWhiteSpace(m.ExternalRoomId)))
                throw new DomainException("invalid_input", "External room id is required for every mapping.");
            if (input.GroupBy(m => (m.RoomTypeId, m.RatePlanId)).Any(g => g.Count() > 1))
                throw new DomainException("invalid_input", "Each room type / rate plan can be mapped only once.");
            foreach (var m in input.Where(m => m.ImportUrl is not null))
                if (!Uri.TryCreate(m.ImportUrl, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http"))
                    throw new DomainException("invalid_input", $"Invalid calendar URL '{m.ImportUrl}'.");

            db.ChannelMappings.RemoveRange(c.Mappings);
            await db.SaveChangesAsync(ct);
            foreach (var m in input)
                db.ChannelMappings.Add(new ChannelMapping
                {
                    ChannelConnectionId = id, RoomTypeId = m.RoomTypeId, RatePlanId = m.RatePlanId,
                    ExternalRoomId = m.ExternalRoomId.Trim(), ExternalRateId = m.ExternalRateId?.Trim(),
                    ImportUrl = string.IsNullOrWhiteSpace(m.ImportUrl) ? null : m.ImportUrl.Trim(),
                });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/test", (Guid id, ChannelSyncService sync, CancellationToken ct) => sync.TestAsync(id, ct));

        g.MapPost("/{id:guid}/pull", (Guid id, ChannelSyncService sync, CancellationToken ct) => sync.PullReservationsAsync(id, ct));

        g.MapPost("/{id:guid}/push", async (Guid id, int? days, ChannelSyncService sync, CancellationToken ct) =>
        {
            var error = await sync.PushFullAsync(id, days ?? ChannelSyncService.PushHorizonDays, ct);
            return error is null ? Results.Ok(new { Success = true }) : throw new ChannelException(error);
        });

        g.MapGet("/{id:guid}/logs", async (Guid id, int? take, AppDbContext db, CancellationToken ct) =>
            (await db.ChannelSyncLogs.AsNoTracking().Where(l => l.ChannelConnectionId == id)
                .ToListAsync(ct)) // ordered client side: DateTimeOffset ordering is provider-dependent
                .OrderByDescending(l => l.CreatedAt).Take(Math.Clamp(take ?? 50, 1, 500)));
    }

    private static ChannelDto ToDto(ChannelConnection c, HttpRequest http, HotelOptions hotel)
    {
        var settings = c.Settings.Where(kv => kv.Key != ICalConnector.ExportTokenSetting).ToDictionary();
        IReadOnlyList<ICalExportLink>? exports = null;
        if (c.Channel == ChannelType.ICal && c.Settings.TryGetValue(ICalConnector.ExportTokenSetting, out var token))
        {
            var baseUrl = string.IsNullOrWhiteSpace(hotel.PublicApiUrl) ? $"{http.Scheme}://{http.Host}" : hotel.PublicApiUrl.TrimEnd('/');
            exports = c.Mappings.Select(m => new ICalExportLink(m.RoomTypeId, $"{baseUrl}/api/ical/{c.Id}/{m.RoomTypeId}.ics?token={token}")).ToList();
        }
        return new ChannelDto(c.Id, c.Channel, c.Name, c.IsEnabled, c.ExternalPropertyId, settings, c.EncryptedSecret is not null,
            c.CommissionPercent, c.RateMarkupPercent, c.LastReservationPullAt, c.LastAriPushAt, c.LastError,
            c.Mappings.Select(m => new MappingInput(m.RoomTypeId, m.RatePlanId, m.ExternalRoomId, m.ExternalRateId, m.ImportUrl)).ToList(),
            exports);
    }

    public record ChannelDto(Guid Id, ChannelType Channel, string Name, bool IsEnabled, string? ExternalPropertyId,
        Dictionary<string, string> Settings, bool HasSecret, decimal CommissionPercent, decimal RateMarkupPercent,
        DateTimeOffset? LastReservationPullAt, DateTimeOffset? LastAriPushAt, string? LastError,
        IReadOnlyList<MappingInput> Mappings, IReadOnlyList<ICalExportLink>? ICalExports);

    public record ICalExportLink(Guid RoomTypeId, string Url);

    public record MappingInput(Guid RoomTypeId, Guid? RatePlanId, string ExternalRoomId, string? ExternalRateId, string? ImportUrl);

    public record ChannelInput(ChannelType Channel, string Name, bool IsEnabled, string? ExternalPropertyId,
        Dictionary<string, string>? Settings, string? Secret, decimal CommissionPercent, decimal RateMarkupPercent)
    {
        public void Apply(ChannelConnection c, ISecretProtector secrets)
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new DomainException("invalid_input", "Name is required.");
            if (CommissionPercent is < 0 or > 100 || RateMarkupPercent is < -50 or > 200)
                throw new DomainException("invalid_input", "Commission / markup out of range.");
            c.Name = Name.Trim();
            c.IsEnabled = IsEnabled;
            c.ExternalPropertyId = string.IsNullOrWhiteSpace(ExternalPropertyId) ? null : ExternalPropertyId.Trim();
            var keep = c.Settings.Where(kv => kv.Key == ICalConnector.ExportTokenSetting).ToList();
            c.Settings = (Settings ?? []).Where(kv => kv.Key != "secret" && kv.Key != ICalConnector.ExportTokenSetting)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
            foreach (var kv in keep) c.Settings[kv.Key] = kv.Value;
            // Empty secret = keep the stored one (the UI never receives it back).
            if (!string.IsNullOrEmpty(Secret)) c.EncryptedSecret = secrets.Protect(Secret);
            c.CommissionPercent = CommissionPercent;
            c.RateMarkupPercent = RateMarkupPercent;
        }
    }
}
