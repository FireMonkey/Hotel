using System.Diagnostics;
using Hotel.Application.Abstractions;
using Hotel.Application.Ari;
using Hotel.Application.Availability;
using Hotel.Application.Bookings;
using Hotel.Application.Common;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hotel.Application.Channels;

public class ChannelSyncService(
    IAppDbContext db,
    IEnumerable<IChannelConnector> connectors,
    AriService ari,
    AvailabilityService availability,
    IInventoryLock inventoryLock,
    ISecretProtector secrets,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<ChannelSyncService> logger)
{
    /// <summary>How far ahead ARI is pushed to channels.</summary>
    public const int PushHorizonDays = 365;
    public const int MaxOutboxAttempts = 10;

    public IChannelConnector Connector(ChannelType type) =>
        connectors.FirstOrDefault(c => c.Type == type) ?? throw new ChannelException($"No connector registered for {type}.");

    public IReadOnlyList<ChannelDescriptor> Describe() => connectors.Select(c => c.Describe()).ToList();

    public async Task<ConnectionTestResult> TestAsync(Guid connectionId, CancellationToken ct)
    {
        var (conn, ctx) = await LoadContext(connectionId, ct);
        try
        {
            var result = await Connector(conn.Channel).TestConnectionAsync(ctx, ct);
            await Log(conn, SyncDirection.Pull, "test", result.Success, 0, result.Message, 0, ct);
            return result;
        }
        catch (Exception ex) when (ex is ChannelException or HttpRequestException or TaskCanceledException)
        {
            await Log(conn, SyncDirection.Pull, "test", false, 0, ex.Message, 0, ct);
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    /// <summary>Process queued ARI changes and push them to every enabled channel.</summary>
    public async Task<int> ProcessAriOutboxAsync(CancellationToken ct)
    {
        var messages = await db.AriOutbox.Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.Id).Take(500).ToListAsync(ct);
        if (messages.Count == 0) return 0;

        var today = Today();
        var horizon = today.AddDays(PushHorizonDays);
        var groups = messages.GroupBy(m => m.RoomTypeId).ToList();

        var connections = await db.ChannelConnections.AsNoTracking().Include(c => c.Mappings)
            .Where(c => c.IsEnabled).ToListAsync(ct);

        foreach (var group in groups)
        {
            var from = Max(group.Min(m => m.From), today);
            var to = Min(group.Max(m => m.To), horizon);
            string? error = null;
            if (from < to)
            {
                foreach (var conn in connections.Where(c => c.Mappings.Any(m => m.RoomTypeId == group.Key)))
                {
                    error ??= await PushAsync(conn, [group.Key], from, to, ct);
                }
            }
            foreach (var m in group)
            {
                m.Attempts++;
                if (error is null || m.Attempts >= MaxOutboxAttempts) m.ProcessedAt = clock.GetUtcNow();
                m.LastError = error;
            }
        }
        await db.SaveChangesAsync(ct);
        return messages.Count;
    }

    /// <summary>Full ARI resync for one connection (use after mapping changes).</summary>
    public async Task<string?> PushFullAsync(Guid connectionId, int days, CancellationToken ct)
    {
        var conn = await db.ChannelConnections.AsNoTracking().Include(c => c.Mappings)
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct) ?? throw new KeyNotFoundException("Channel not found.");
        var from = Today();
        return await PushAsync(conn, conn.Mappings.Select(m => m.RoomTypeId).Distinct().ToList(),
            from, from.AddDays(Math.Clamp(days, 1, PushHorizonDays)), ct);
    }

    /// <returns>null on success, error message otherwise.</returns>
    private async Task<string?> PushAsync(ChannelConnection conn, IReadOnlyCollection<Guid> roomTypeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var connector = Connector(conn.Channel);
        if (!connector.Describe().SupportsAriPush || roomTypeIds.Count == 0) return null;

        var sw = Stopwatch.StartNew();
        try
        {
            var snapshot = await ari.BuildSnapshotAsync(roomTypeIds, from, to, ct);
            if (conn.RateMarkupPercent != 0)
            {
                var factor = 1 + conn.RateMarkupPercent / 100m;
                snapshot = snapshot with { Rates = snapshot.Rates.Select(r => r with { Price = Math.Round(r.Price * factor, 2) }).ToList() };
            }
            var ctx = new ChannelContext(conn, secrets.Unprotect(conn.EncryptedSecret), conn.Mappings);
            await connector.PushAriAsync(ctx, snapshot, ct);
            await Log(conn, SyncDirection.Push, "ari", true, snapshot.Availability.Count + snapshot.Rates.Count,
                $"{from:yyyy-MM-dd} → {to:yyyy-MM-dd}", sw.ElapsedMilliseconds, ct, lastAri: true);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ARI push to {Channel} failed", conn.Name);
            await Log(conn, SyncDirection.Push, "ari", false, 0, ex.Message, sw.ElapsedMilliseconds, ct);
            return $"{conn.Name}: {ex.Message}";
        }
    }

    public async Task PullAllAsync(CancellationToken ct)
    {
        var ids = await db.ChannelConnections.Where(c => c.IsEnabled).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await PullReservationsAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Reservation pull failed for {Id}", id); }
        }
    }

    public async Task<PullResult> PullReservationsAsync(Guid connectionId, CancellationToken ct)
    {
        var (conn, ctx) = await LoadContext(connectionId, ct);
        var connector = Connector(conn.Channel);
        if (!connector.Describe().SupportsReservationPull) return new PullResult(0, 0, 0, []);

        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        int created = 0, modified = 0, cancelled = 0;
        try
        {
            var feed = await connector.FetchReservationsAsync(ctx, ct);
            var processed = new List<ExternalReservation>();
            foreach (var r in feed.Reservations)
            {
                try
                {
                    var outcome = await ImportAsync(conn, ctx, r, ct);
                    switch (outcome)
                    {
                        case ImportOutcome.Created: created++; break;
                        case ImportOutcome.Modified: modified++; break;
                        case ImportOutcome.Cancelled: cancelled++; break;
                    }
                    processed.Add(r);
                }
                catch (ChannelException ex)
                {
                    // Not acknowledged → channel will re-deliver once the problem (e.g. mapping) is fixed.
                    errors.Add($"{r.ExternalId}: {ex.Message}");
                }
            }

            if (feed.IsFullSnapshot)
                cancelled += await CancelMissingAsync(conn, feed.Reservations.Select(r => r.ExternalId).ToHashSet(), ct);

            if (processed.Count > 0) await connector.AcknowledgeAsync(ctx, processed, ct);

            var tracked = await db.ChannelConnections.FirstAsync(c => c.Id == conn.Id, ct);
            tracked.LastReservationPullAt = clock.GetUtcNow();
            tracked.LastError = errors.Count > 0 ? string.Join("; ", errors) : null;
            await db.SaveChangesAsync(ct);
            await Log(conn, SyncDirection.Pull, "reservations", errors.Count == 0, created + modified + cancelled,
                $"new {created}, modified {modified}, cancelled {cancelled}" + (errors.Count > 0 ? $"; errors: {string.Join("; ", errors)}" : ""),
                sw.ElapsedMilliseconds, ct);
        }
        catch (Exception ex) when (ex is ChannelException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            await Log(conn, SyncDirection.Pull, "reservations", false, 0, ex.Message, sw.ElapsedMilliseconds, ct);
            throw new ChannelException(ex.Message, ex);
        }
        return new PullResult(created, modified, cancelled, errors);
    }

    private enum ImportOutcome { Ignored, Created, Modified, Cancelled }

    private async Task<ImportOutcome> ImportAsync(ChannelConnection conn, ChannelContext ctx, ExternalReservation r, CancellationToken ct)
    {
        var existing = await db.Bookings.FirstOrDefaultAsync(b => b.ChannelConnectionId == conn.Id && b.ExternalReference == r.ExternalId, ct);

        if (r.Status == ExternalReservationStatus.Cancelled)
        {
            if (existing is null || existing.Status == BookingStatus.Cancelled) return ImportOutcome.Ignored;
            existing.Cancel($"Cancelled on {conn.Name}");
            AriOutbox.Enqueue(db, existing.RoomTypeId, existing.CheckIn, existing.CheckOut);
            await db.SaveChangesAsync(ct);
            await notifications.BookingCancelledAsync(existing, ct);
            return ImportOutcome.Cancelled;
        }

        var mapping = (r.ExternalRateId is not null
                          ? ctx.Mappings.FirstOrDefault(m => m.ExternalRoomId == r.ExternalRoomId && m.ExternalRateId == r.ExternalRateId)
                          : null)
                      ?? ctx.Mappings.FirstOrDefault(m => m.ExternalRoomId == r.ExternalRoomId)
                      ?? throw new ChannelException($"Room '{r.ExternalRoomId}' is not mapped.");
        if (r.CheckOut <= r.CheckIn) throw new ChannelException("Invalid stay dates.");

        var lockIds = new HashSet<Guid> { mapping.RoomTypeId };
        if (existing is not null) lockIds.Add(existing.RoomTypeId);
        await using var scope = await inventoryLock.BeginAsync(lockIds.Order(), ct);

        var inventory = await availability.GetInventoryAsync([mapping.RoomTypeId], r.CheckIn, r.CheckOut, ct, existing?.Id);
        var overbooked = inventory[mapping.RoomTypeId].Any(d => d.Available <= 0);
        var commission = r.Commission ?? (conn.CommissionPercent > 0 ? Math.Round(r.TotalAmount * conn.CommissionPercent / 100m, 2) : null);

        var booking = existing ?? new Booking
        {
            Reference = ReferenceGenerator.NewBookingReference(),
            Source = r.Source ?? SourceFor(conn.Channel),
            ExternalReference = r.ExternalId,
            ChannelConnectionId = conn.Id,
            Status = BookingStatus.Confirmed,
            Guest = BookingService.ToGuest(r.Guest),
        };
        if (existing is not null)
        {
            AriOutbox.Enqueue(db, existing.RoomTypeId, existing.CheckIn, existing.CheckOut);
            if (existing.Status == BookingStatus.Cancelled) existing.Status = BookingStatus.Confirmed;
        }

        booking.RoomTypeId = mapping.RoomTypeId;
        booking.RatePlanId = mapping.RatePlanId;
        booking.CheckIn = r.CheckIn;
        booking.CheckOut = r.CheckOut;
        booking.Adults = Math.Max(1, r.Adults);
        booking.Children = Math.Max(0, r.Children);
        booking.Guest = BookingService.ToGuest(r.Guest);
        booking.SpecialRequests = r.Notes;
        booking.TotalAmount = r.TotalAmount;
        booking.Currency = r.Currency;
        booking.CommissionAmount = commission;
        booking.IsOverbooked = overbooked;
        if (r.PaidToChannel) booking.PaymentStatus = PaymentStatus.PaidToChannel;
        booking.UpdatedAt = clock.GetUtcNow();

        if (existing is null) db.Bookings.Add(booking);
        AriOutbox.Enqueue(db, booking.RoomTypeId, booking.CheckIn, booking.CheckOut);
        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);

        if (overbooked)
            logger.LogWarning("OVERBOOKING: {Channel} reservation {ExternalId} imported without free inventory", conn.Name, r.ExternalId);
        if (existing is null) await notifications.BookingCreatedAsync(booking, ct);
        return existing is null ? ImportOutcome.Created : ImportOutcome.Modified;
    }

    private async Task<int> CancelMissingAsync(ChannelConnection conn, HashSet<string> present, CancellationToken ct)
    {
        var today = Today();
        var active = Booking.ActiveStatuses;
        var stale = await db.Bookings
            .Where(b => b.ChannelConnectionId == conn.Id && active.Contains(b.Status) && b.Status != BookingStatus.CheckedIn && b.CheckOut > today)
            .ToListAsync(ct);
        var count = 0;
        foreach (var b in stale.Where(b => b.ExternalReference is not null && !present.Contains(b.ExternalReference)))
        {
            b.Cancel($"Removed from {conn.Name} calendar");
            AriOutbox.Enqueue(db, b.RoomTypeId, b.CheckIn, b.CheckOut);
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<(ChannelConnection, ChannelContext)> LoadContext(Guid id, CancellationToken ct)
    {
        var conn = await db.ChannelConnections.AsNoTracking().Include(c => c.Mappings)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new KeyNotFoundException("Channel not found.");
        return (conn, new ChannelContext(conn, secrets.Unprotect(conn.EncryptedSecret), conn.Mappings));
    }

    private async Task Log(ChannelConnection conn, SyncDirection dir, string op, bool success, int count, string? message, long ms,
        CancellationToken ct, bool lastAri = false)
    {
        db.ChannelSyncLogs.Add(new ChannelSyncLog
        {
            ChannelConnectionId = conn.Id, Direction = dir, Operation = op, Success = success,
            ItemCount = count, Message = message?.Length > 2000 ? message[..2000] : message, DurationMs = ms,
        });
        if (lastAri || !success)
        {
            var tracked = await db.ChannelConnections.FirstAsync(c => c.Id == conn.Id, ct);
            if (lastAri) tracked.LastAriPushAt = clock.GetUtcNow();
            if (!success) tracked.LastError = message;
            else if (lastAri) tracked.LastError = null;
        }
        await db.SaveChangesAsync(ct);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    public static BookingSource SourceFor(ChannelType type) => type switch
    {
        ChannelType.BookingCom => BookingSource.BookingCom,
        ChannelType.Expedia => BookingSource.Expedia,
        ChannelType.ICal => BookingSource.ICal,
        _ => BookingSource.ChannelManager,
    };
}

public record PullResult(int Created, int Modified, int Cancelled, IReadOnlyList<string> Errors);
