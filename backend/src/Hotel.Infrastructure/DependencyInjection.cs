using Hotel.Application;
using Hotel.Application.Abstractions;
using Hotel.Application.Channels;
using Hotel.Infrastructure.Channels.BookingCom;
using Hotel.Infrastructure.Channels.Channex;
using Hotel.Infrastructure.Channels.Expedia;
using Hotel.Infrastructure.Channels.ICal;
using Hotel.Infrastructure.Persistence;
using Hotel.Infrastructure.Services;
using Hotel.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hotel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddApplication();

        services.AddDbContext<AppDbContext>(o =>
            // No retrying execution strategy: inventory operations use explicit transactions (see InventoryLock).
            o.UseNpgsql(config.GetConnectionString("Default")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IInventoryLock, InventoryLock>();
        services.AddScoped<ISecretProtector, SecretProtector>();
        services.AddScoped<INotificationService, LoggingNotificationService>();

        foreach (var name in new[] { BookingComConnector.HttpClientName, ExpediaConnector.HttpClientName, ChannexConnector.HttpClientName, ICalConnector.HttpClientName })
        {
            services.AddHttpClient(name, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(60);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("HotelChannelSync/1.0");
            });
        }
        services.AddScoped<IChannelConnector, BookingComConnector>();
        services.AddScoped<IChannelConnector, ExpediaConnector>();
        services.AddScoped<IChannelConnector, ChannexConnector>();
        services.AddScoped<IChannelConnector, ICalConnector>();

        services.Configure<ChannelSyncOptions>(config.GetSection(ChannelSyncOptions.Section));
        services.AddHostedService<ChannelSyncWorker>();
        return services;
    }
}
