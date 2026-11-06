using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Services;
using TorgLink.LoRa;

namespace TorgLink.Maui;

/// <summary>Starts LoRa (when configured) then <see cref="UserP2pRuntime"/>.</summary>
internal static class P2pBootstrap
{
    public static Task EnsureStartedAsync(
        UserP2pRuntime p2p,
        UserEntity user,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var host = MauiProgram.Services.GetService<MeshtasticTransportHost>();
        if (host == null)
            return p2p.EnsureStartedAsync(user, cancellationToken);
        return LoRaBootstrap.EnsureRunningAsync(host, p2p, user, logger, cancellationToken);
    }
}
