using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Application.Ari;
using Hotel.Application.Availability;
using Microsoft.Extensions.DependencyInjection;

namespace Hotel.Tests;

public class BookingApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static DateOnly Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset);

    private static object BookingBody(Guid ratePlanId, DateOnly checkIn, DateOnly checkOut, string email = "guest@example.com") => new
    {
        ratePlanId, checkIn, checkOut, adults = 2, children = 0,
        guest = new { firstName = "Ayşe", lastName = "Demir", email, phone = "+905550000000", country = "TR" },
    };

    [Fact]
    public async Task Availability_prices_with_daily_rates_and_defaults()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 2, price: 1000m);
        await using (var scope = app.Scope())
        {
            var ari = scope.ServiceProvider.GetRequiredService<AriService>();
            await ari.BulkUpdateRatesAsync(new BulkRateUpdate([plan.Id], Day(20), Day(21), null, 1500m, null, null, null, null), default);
        }

        var res = await app.CreateClient().GetFromJsonAsync<List<RoomTypeAvailability>>(
            $"/api/public/availability?checkIn={Day(19):yyyy-MM-dd}&checkOut={Day(22):yyyy-MM-dd}&roomTypeId={type.Id}", Json);

        var room = Assert.Single(res!);
        Assert.Equal(2, room.AvailableRooms);
        var quote = Assert.Single(room.Rates);
        Assert.Equal(1000m + 1500m + 1000m, quote.TotalPrice);
    }

    [Fact]
    public async Task Restrictions_hide_rate_plan()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        await using (var scope = app.Scope())
        {
            var ari = scope.ServiceProvider.GetRequiredService<AriService>();
            await ari.BulkUpdateRatesAsync(new BulkRateUpdate([plan.Id], Day(30), Day(31), null, null, 3, null, null, null), default);
            await ari.BulkUpdateRatesAsync(new BulkRateUpdate([plan.Id], Day(40), Day(41), null, null, null, true, null, null), default);
        }
        var client = app.CreateClient();
        async Task<int> Count(int from, int to) => (await client.GetFromJsonAsync<List<RoomTypeAvailability>>(
            $"/api/public/availability?checkIn={Day(from):yyyy-MM-dd}&checkOut={Day(to):yyyy-MM-dd}&roomTypeId={type.Id}", Json))!.Count;

        Assert.Equal(0, await Count(30, 32)); // min stay 3 on arrival day
        Assert.Equal(1, await Count(30, 33));
        Assert.Equal(0, await Count(39, 42)); // stop sell inside the stay
    }

    [Fact]
    public async Task Last_room_cannot_be_sold_twice()
    {
        var (_, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var client = app.CreateClient();

        var first = await client.PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(5), Day(7)));
        var second = await client.PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(6), Day(8)));
        var adjacent = await client.PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(7), Day(9)));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode); // check-out day is free again
    }

    [Fact]
    public async Task Concurrent_requests_for_last_room_sell_exactly_one()
    {
        var (_, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var tasks = Enumerable.Range(0, 8).Select(i => app.CreateClient()
            .PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(50), Day(52), $"g{i}@example.com")));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Booking_queues_ari_update_and_guest_can_cancel()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var client = app.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(60), Day(62), "c@example.com")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var reference = created.GetProperty("reference").GetString();

        var wrongEmail = await client.PostAsJsonAsync("/api/public/bookings/lookup", new { reference, email = "x@example.com" });
        Assert.Equal(HttpStatusCode.NotFound, wrongEmail.StatusCode);

        var cancelled = await client.PostAsJsonAsync("/api/public/bookings/cancel", new { reference, email = "C@example.com" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var again = await client.PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(60), Day(62)));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode); // inventory released
    }

    [Fact]
    public async Task Public_room_types_show_lowest_rate_plan_price()
    {
        var (type, _) = await app.SeedRoomTypeAsync(rooms: 1, price: 2000m);
        var list = await app.CreateClient().GetFromJsonAsync<List<JsonElement>>("/api/public/room-types");
        var room = list!.Single(r => r.GetProperty("id").GetGuid() == type.Id);
        Assert.Equal(2000m, room.GetProperty("fromPrice").GetDecimal());
    }

    [Fact]
    public async Task Admin_endpoints_require_authentication()
    {
        var anonymous = await app.CreateClient().GetAsync("/api/admin/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var admin = await app.AdminClientAsync();
        var res = await admin.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_problem_details()
    {
        var (_, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var res = await app.CreateClient().PostAsJsonAsync("/api/public/bookings", BookingBody(plan.Id, Day(10), Day(9)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("invalid_stay", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
