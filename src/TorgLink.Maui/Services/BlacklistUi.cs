using ShortP2P.Client.Services;
using TorgLink.Localization;

namespace TorgLink.Maui.Services;

internal static class BlacklistUi
{
    public static async Task<bool> ConfirmAndBlockAsync(
        Page host,
        PeerBlacklist blacklist,
        int userId,
        string networkId,
        string nickname)
    {
        var id = ChatRepository.CanonicalPeerNetworkId(networkId);
        if (userId <= 0 || id.Length == 0)
            return false;

        await blacklist.EnsureLoadedAsync(userId).ConfigureAwait(true);
        if (blacklist.IsBlocked(userId, id))
            return true;

        var label = string.IsNullOrWhiteSpace(nickname) ? id : nickname.Trim();
        var ok = await host.DisplayAlert(
            LocalizationUtils.GetStringByKey("blacklist.add_title"),
            LocalizationUtils.GetStringByKeyWithFormat("blacklist.add_body", label),
            LocalizationUtils.GetStringByKey("blacklist.add"),
            LocalizationUtils.GetStringByKey("cancel")).ConfigureAwait(true);
        if (!ok)
            return false;

        await blacklist.AddAsync(userId, id, label).ConfigureAwait(true);
        return true;
    }
}

