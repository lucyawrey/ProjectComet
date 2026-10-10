using System.Net.WebSockets;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Comet.Server;

public static class CometServerExtensions
{
    private static readonly ConnectionLimits DefaultLimits = new();

    /// <summary>
    /// Accepts game connections at <paramref name="pattern"/>. Needs <c>UseWebSockets()</c>, and a
    /// <see cref="ConnectionRegistry"/>, <see cref="IConnectionHandler"/> and <see cref="TickLoop"/> in services,
    /// and optionally <see cref="ConnectionLimits"/>. Connections over the limits are refused (503).
    /// </summary>
    public static IEndpointConventionBuilder MapCometWebSocket(this IEndpointRouteBuilder endpoints, string pattern) =>
        endpoints.Map(pattern, async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var services = context.RequestServices;
            var registry = services.GetRequiredService<ConnectionRegistry>();
            var limits = services.GetService<ConnectionLimits>() ?? DefaultLimits;
            var address = context.Connection.RemoteIpAddress;
            if (!registry.TryReserve(address, limits))
            {
                registry.Stats.AddConnectionRefused();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = false,
                    KeepAliveInterval = limits.KeepAliveInterval,
                    KeepAliveTimeout = limits.KeepAliveTimeout,
                });
                var connection = new Connection(
                    socket,
                    registry,
                    services.GetRequiredService<IConnectionHandler>(),
                    services.GetRequiredService<TickLoop>());

                // Close connections as the server stops, rather than leave them for the host's shutdown timeout.
                using var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(connection.Shutdown);
                await connection.RunAsync(context.RequestAborted);
            }
            finally
            {
                registry.Release(address);
            }
        });
}
