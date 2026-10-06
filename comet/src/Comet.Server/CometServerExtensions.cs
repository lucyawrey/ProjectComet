using System.Net.WebSockets;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Comet.Server;

public static class CometServerExtensions
{
    /// <summary>
    /// Accepts game connections at <paramref name="pattern"/>. Needs <c>UseWebSockets()</c>, and a
    /// <see cref="ConnectionRegistry"/>, <see cref="IConnectionHandler"/> and <see cref="TickLoop"/> in services.
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
            using var socket = await context.WebSockets.AcceptWebSocketAsync(
                new WebSocketAcceptContext { DangerousEnableCompression = false });
            var connection = new Connection(
                socket,
                services.GetRequiredService<ConnectionRegistry>(),
                services.GetRequiredService<IConnectionHandler>(),
                services.GetRequiredService<TickLoop>());
            await connection.RunAsync(context.RequestAborted);
        });
}
