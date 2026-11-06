using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client;
using ShortP2P.Client.Services;
using ShortP2P.Client.Transport;
using ShortP2P.LoRa;
using ShortP2P.LoRa.Directory;
using ShortP2P.LoRa.Jurisdiction;
using ShortP2P.Transport.Abstractions;
using LoRaTransportKind = ShortP2P.LoRa.TransportKind;

namespace TorgLink.LoRa;

/// <summary>
///     TorgLink host for <see cref="MeshatasticTransport"/>: lifecycle, settings, MeshDirectory → chat endpoints.
///     Presence/discovery UDP is not carried on LoRa (REQ-WIRE-1); peers come from MeshDirectory snapshots.
/// </summary>
public sealed class MeshtasticTransportHost : IAdditionalTransportProvider, IAsyncDisposable
{
    /// <summary>SMT-1 / LoRa docs: client-to-client binary ceiling when LoRa is the active path.</summary>
    public const int MaxBinaryBytes = 20 * 1024;

    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly LoRaTransportSettingsStore _store;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _transportSync = new();

    private MeshatasticTransport? _transport;
    private LoRaTransportSettings _settings = new();
    private Func<byte[], bool>? _isServerNetworkId;
    private int _directorySyncRunning;

    public MeshtasticTransportHost(
        AuthService auth,
        ChatRepository chats,
        LoRaTransportSettingsStore store,
        ILoggerFactory? loggerFactory = null)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _chats = chats ?? throw new ArgumentNullException(nameof(chats));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(nameof(MeshtasticTransportHost));
    }

    /// <summary>Raised after MeshDirectory content changes (UI refresh).</summary>
    public event EventHandler? DirectoryChanged;

    public LoRaTransportSettings CurrentSettings => _settings;

    public bool IsRunning
    {
        get
        {
            lock (_transportSync)
                return _transport != null;
        }
    }

    public MeshDirectory? Directory
    {
        get
        {
            lock (_transportSync)
                return _transport?.Directory;
        }
    }

    /// <summary>Optional: which NetworkIds are messenger servers (REQ-DIR-2 subject).</summary>
    public void SetServerNetworkIdLookup(Func<byte[], bool>? isServerNetworkId) =>
        _isServerNetworkId = isServerNetworkId;

    public async Task<LoRaTransportSettings> LoadSettingsAsync()
    {
        _settings = await _store.LoadAsync().ConfigureAwait(false);
        return _settings;
    }

    public async Task SaveSettingsAsync(LoRaTransportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _store.SaveAsync(settings).ConfigureAwait(false);
        _settings = settings;
    }

    /// <summary>
    ///     Start or restart the transport for the current user when Enabled + COM + jurisdiction allow it.
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _settings = await _store.LoadAsync().ConfigureAwait(false);
            var user = _auth.CurrentUser;
            if (user == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ComPort))
            {
                await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var jurisdiction = JurisdictionCatalog.Find(_settings.JurisdictionCode) ?? JurisdictionCatalog.Unset;
            if (jurisdiction.IsUnset)
                _logger.LogInformation("LoRa enabled but jurisdiction is UNSET — transport will not TX (REQ-RF-2)");

            await StopCoreAsync(cancellationToken).ConfigureAwait(false);

            var options = new MeshatasticTransportOptions
            {
                ComPort = _settings.ComPort.Trim(),
                BaudRate = _settings.BaudRate > 0 ? _settings.BaudRate : 115200,
                Jurisdiction = jurisdiction,
                LocalNetworkId = CompressedNetworkId.FromShortString(user.NetworkIdShort).ToWireBytes(),
                IsServerNetworkId = net => _isServerNetworkId?.Invoke(net) == true
            };

            var transport = new MeshatasticTransport(options, _logger);
            transport.Directory.Changed += OnDirectoryChanged;
            try
            {
                await transport.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                transport.Directory.Changed -= OnDirectoryChanged;
                await transport.DisposeAsync().ConfigureAwait(false);
                _logger.LogWarning(ex, "Failed to start Meshatastic transport on {ComPort}", options.ComPort);
                throw;
            }

            lock (_transportSync)
                _transport = transport;

            _logger.LogInformation("Meshatastic transport started on {ComPort} (node {Node})",
                options.ComPort, transport.LocalMeshId?.ToHex() ?? "?");
            QueueDirectorySync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        MeshatasticTransport? transport;
        lock (_transportSync)
        {
            transport = _transport;
            _transport = null;
        }

        if (transport == null)
            return;

        transport.Directory.Changed -= OnDirectoryChanged;
        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Meshatastic transport dispose failed");
        }
    }

    public bool IsEnabled(byte kind) =>
        kind == LoRaTransportKind.Meshtastic && _settings.Enabled && IsRunning;

    public IReadOnlyList<ITransport> GetDataTransports()
    {
        lock (_transportSync)
        {
            if (_transport == null || !_settings.Enabled)
                return [];
            return [_transport];
        }
    }

    public ITransport? ResolveOutbound(TransportAddress destination)
    {
        if (destination.Kind != LoRaTransportKind.Meshtastic || !_settings.Enabled)
            return null;
        lock (_transportSync)
            return _transport;
    }

    public void RememberPeer(byte[] networkIdWire12, TransportAddress from)
    {
        if (from.Kind != LoRaTransportKind.Meshtastic)
            return;
        MeshatasticTransport? transport;
        lock (_transportSync)
            transport = _transport;
        if (transport == null)
            return;
        try
        {
            var meshId = MeshatasticTransportAddress.FromTransportAddress(from);
            transport.Remember(networkIdWire12, meshId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RememberPeer failed for MeshId");
        }
    }

    public IReadOnlyList<LoRaMeshPeerInfo> ListPeers()
    {
        var dir = Directory;
        if (dir == null)
            return [];

        var list = new List<LoRaMeshPeerInfo>();
        foreach (var b in dir.Snapshot())
        {
            if (b.Subject == MeshSubject.Self)
                continue;
            string shortId;
            try
            {
                shortId = CompressedNetworkId.FromWireBytes(b.NetworkId).ToShortString();
            }
            catch
            {
                shortId = b.NetworkIdHex;
            }

            list.Add(new LoRaMeshPeerInfo(
                shortId,
                b.MeshIdHex,
                b.Subject.ToString(),
                b.IsActive,
                b.Confirmed,
                b.LastSeenUtc));
        }

        return list;
    }

    private void OnDirectoryChanged()
    {
        DirectoryChanged?.Invoke(this, EventArgs.Empty);
        QueueDirectorySync();
    }

    private void QueueDirectorySync()
    {
        if (Interlocked.Exchange(ref _directorySyncRunning, 1) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150).ConfigureAwait(false);
                await SyncDirectoryToChatsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MeshDirectory → chat endpoints sync failed");
            }
            finally
            {
                Interlocked.Exchange(ref _directorySyncRunning, 0);
            }
        });
    }

    private async Task SyncDirectoryToChatsAsync()
    {
        var user = _auth.CurrentUser;
        var dir = Directory;
        if (user == null || dir == null)
            return;

        var byNetwork = new Dictionary<string, List<TransportAddress>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in dir.Snapshot())
        {
            if (!b.IsActive || b.Subject == MeshSubject.Self)
                continue;
            string shortId;
            try
            {
                shortId = CompressedNetworkId.FromWireBytes(b.NetworkId).ToShortString();
            }
            catch
            {
                continue;
            }

            if (!byNetwork.TryGetValue(shortId, out var list))
            {
                list = [];
                byNetwork[shortId] = list;
            }

            list.Add(MeshatasticTransportAddress.ToTransportAddress(new MeshId(b.MeshId)));
        }

        foreach (var (shortId, endpoints) in byNetwork)
        {
            var chat = await _chats.FindChatByPeerNetworkIdAsync(user.Id, shortId).ConfigureAwait(false);
            if (chat == null)
                continue;
            if (await _chats.ReplaceChatTransportEndpointsOfKindAsync(
                    chat.Id, LoRaTransportKind.Meshtastic, endpoints).ConfigureAwait(false))
            {
                _chats.NotifyChatListChanged();
                _logger.LogDebug("Updated LoRa endpoints for chat {ChatId} peer {Peer}", chat.Id, shortId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
