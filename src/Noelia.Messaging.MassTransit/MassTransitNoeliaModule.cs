using System.Reflection;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Messaging;

namespace Noelia.Messaging.MassTransit;

/// <summary>Exposes MassTransit messaging in the Noelia composition.</summary>
public static class MassTransitNoeliaModule
{
    public static NoeliaModule Module => new("Messaging.MassTransit");

    public static NoeliaBuilder UseMassTransitMessaging(
        this NoeliaBuilder noelia,
        params Assembly[] consumerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(noelia);
        ArgumentNullException.ThrowIfNull(consumerAssemblies);

        return noelia.Use(
            Module,
            builder => builder.Services
                .AddMessaging(builder.Configuration, consumerAssemblies)
                .AddEventBus(),
            contract => contract.Provides<IEventBus>(
                "Noelia.Messaging.MassTransit", "UseMassTransitMessaging(assemblies)"));
    }
}
