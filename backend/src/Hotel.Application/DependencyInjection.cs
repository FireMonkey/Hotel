using Hotel.Application.Ari;
using Hotel.Application.Availability;
using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Application.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Hotel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AvailabilityService>();
        services.AddScoped<BookingService>();
        services.AddScoped<AriService>();
        services.AddScoped<ChannelSyncService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
