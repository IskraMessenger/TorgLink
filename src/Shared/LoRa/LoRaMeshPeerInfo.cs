namespace TorgLink.LoRa;

/// <summary>UI-facing MeshDirectory binding.</summary>
public sealed record LoRaMeshPeerInfo(
    string NetworkIdShort,
    string MeshIdHex,
    string Subject,
    bool IsActive,
    bool Confirmed,
    DateTime LastSeenUtc);
