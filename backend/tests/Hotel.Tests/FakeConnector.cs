using System.Collections.Concurrent;
using Hotel.Application.Ari;
using Hotel.Application.Channels;
using Hotel.Domain.Channels;

namespace Hotel.Tests;

public class FakeConnector : IChannelConnector
{
    public const ChannelType FakeType = ChannelType.Channex;

    public ChannelType Type => FakeType;
    public ConcurrentQueue<ExternalReservation> Inbox { get; } = new();
    public List<AriSnapshot> Pushed { get; } = [];
    public List<ExternalReservation> Acknowledged { get; } = [];

    public ChannelDescriptor Describe() => new(Type, "Fake", "Test double", true, true, false, [], null);

    public Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, "ok"));

    public Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct)
    {
        lock (Pushed) Pushed.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct)
    {
        var list = new List<ExternalReservation>();
        while (Inbox.TryDequeue(out var r)) list.Add(r);
        return Task.FromResult(new ReservationFetchResult(list));
    }

    public Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct)
    {
        lock (Acknowledged) Acknowledged.AddRange(processed);
        return Task.CompletedTask;
    }
}
