using Hotel.Application.Ari;

namespace Hotel.Api.Endpoints;

public static class AdminAriEndpoints
{
    public static void MapAdminAriEndpoints(this RouteGroupBuilder admin)
    {
        var g = admin.MapGroup("/ari").WithTags("Admin: Rates & availability");

        g.MapGet("/calendar", (DateOnly from, DateOnly to, AriService svc, CancellationToken ct) => svc.GetCalendarAsync(from, to, ct));

        g.MapPut("/rates", async (BulkRateUpdate req, AriService svc, CancellationToken ct) =>
            new { Updated = await svc.BulkUpdateRatesAsync(req, ct) });

        g.MapPut("/inventory", async (InventoryUpdate req, AriService svc, CancellationToken ct) =>
        {
            await svc.SetInventoryAsync(req, ct);
            return Results.NoContent();
        });
    }
}
