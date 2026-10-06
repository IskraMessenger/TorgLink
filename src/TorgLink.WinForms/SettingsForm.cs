using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Client.Services.MessengerServers;
using ShortP2P.Crypto;
using ShortP2P.Discovery;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Img = SixLabors.ImageSharp.Image;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace TorgLink.WinForms;

/// <summary>
/// Maui Settings subset that works without BLE/camera: profile (avatar/about inline), LAN, routing, economy, keys, about.
/// </summary>
public sealed partial class SettingsForm : AppForm
{
    private const int AvatarDimension = 512;

    private readonly AuthService _auth = null!;
    private readonly ChatRepository _chats = null!;
    private readonly ChatSessionCache _sessions = null!;
    private readonly MessengerServerSyncService _sync = null!;
    private readonly P2pRoutingSettings _live = null!;
    private readonly P2pRoutingSettingsStore _store = null!;
    private readonly string _appRoot = null!;
    private readonly ILogger<SettingsForm> _logger = null!;

    private byte[]? _avatarBytes;

    public SettingsForm()
    {
        InitializeComponent();
    }

    public SettingsForm(
        AuthService auth,
        ChatRepository chats,
        ChatSessionCache sessions,
        MessengerServerSyncService sync,
        P2pRoutingSettings live,
        P2pRoutingSettingsStore store,
        ILogger<SettingsForm> logger)
        : this()
    {
        _auth = auth;
        _chats = chats;
        _sessions = sessions;
        _sync = sync;
        _live = live;
        _store = store;
        _logger = logger;
        _appRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TorgLink", "WinForms");

        foreach (var mode in new[]
                 {
                     TrafficQualityMode.Normal, TrafficQualityMode.Economy, TrafficQualityMode.UltraEconomy
                 })
            _economy.Items.Add(new EconomyItem(mode));

        foreach (var p in LinkTechnologyPresetExtensions.AllPresets)
            _link.Items.Add(new LinkItem(p));

        _economy.SelectedIndexChanged += (_, _) => UpdateEconomyHint();

        // Runtime values from shared constants (Designer.cs keeps plain literals for the WinForms designer)
        _aboutLabel.Text = $"О себе (до {PeerProfileLimits.MaxAboutMeChars} символов):";
        _aboutMe.MaxLength = PeerProfileLimits.MaxAboutMeChars;
        _hint.Text =
            $"Аватар — квадратная обрезка {AvatarDimension}×{AvatarDimension}, до {PeerProfileLimits.MaxAvatarBytes / 1024} КБ. " +
            "Данные хранятся только локально и отдаются пирам при скане сети.";

        _loadAvatar.Click += (_, _) => OnLoadAvatar();
        _clearAvatar.Click += (_, _) =>
        {
            _avatarBytes = null;
            ApplyAvatarPreview(null);
        };
        _aboutMe.TextChanged += (_, _) => UpdateAboutCounter();

        _save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _keys.Click += (_, _) => CopyKeys();
        _about.Click += (_, _) => MessageBox.Show(this,
            "Mesh-мессенджер.\nTorgLink.WinForms 0.2 (.NET Framework 4.7.2)\nWindows 7 SP1+\nБез BLE и камеры. QR — из файла.",
            "TorgLink", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Load += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        UpdateProfileSummary();
        _udpPort.Text = _auth.CurrentUser?.DataUdpPort.ToString() ?? "—";
        _storage.Text = FormatStorage(_appRoot);
        LoadProfileIntoUiBestEffort();

        var s = await _store.LoadAsync().ConfigureAwait(true);
        RoutingSettingsLive.Overlay(_live, s);

        _lan.Checked = _live.EnableUdpTransport;
        _shareRoutes.Checked = _live.AdvertisedPeerCapabilities.HasFlag(PresencePeerCapabilities.PeerSearch);
        _hops.Value = Clamp(_live.MaxSearchHops, 1, 3);
        _attempts.Value = Clamp(_live.SendFailureSearchAttempts, 1, 20);
        _delayMs.Value = Clamp((decimal)_live.SendFailureRetryDelay.TotalMilliseconds, 0, 3_600_000);
        _timeoutMs.Value = Clamp((decimal)_live.SearchWaitTimeout.TotalMilliseconds, 500, 120_000);

        SelectEconomy(_live.TrafficQuality);
        var li = Array.IndexOf(LinkTechnologyPresetExtensions.AllPresets, _live.LinkTechnology);
        _link.SelectedIndex = li >= 0 ? li : 0;
        UpdateEconomyHint();
    }

    private void UpdateProfileSummary()
    {
        var u = _auth.CurrentUser;
        var about = u != null && !string.IsNullOrWhiteSpace(u.AboutMe)
            ? $" · {TrimAbout(u.AboutMe, 60)}"
            : "";
        _profile.Text = u == null ? "Не выполнен вход" : $"{u.Nickname}  ·  {u.NetworkIdShort}{about}";
    }

    private void LoadProfileIntoUiBestEffort()
    {
        try
        {
            var user = _auth.CurrentUser;
            _aboutMe.Text = user?.AboutMe ?? "";
            _avatarBytes = user?.Avatar;
            ApplyAvatarPreview(_avatarBytes);
            UpdateAboutCounter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load own profile into UI (best-effort); continuing with empty fields");
            _aboutMe.Text = "";
            _avatarBytes = null;
            ApplyAvatarPreview(null);
            UpdateAboutCounter();
        }
    }

    private void UpdateAboutCounter()
    {
        _aboutCounter.Text = $"{_aboutMe.Text.Length} / {PeerProfileLimits.MaxAboutMeChars}";
    }

    private void OnLoadAvatar()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Выбор аватара",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var raw = File.ReadAllBytes(dlg.FileName);
            if (!TryCropTo512(raw, out var cropped, out var err))
            {
                MessageBox.Show(this, err ?? "Не удалось подготовить изображение.", "Аватар",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            byte[]? prepared = cropped;
            if (cropped!.Length > PeerProfileLimits.MaxAvatarBytes)
            {
                var sizeKb = Math.Max(1, (cropped.Length + 1023) / 1024);
                var limitKb = PeerProfileLimits.MaxAvatarBytes / 1024;
                var answer = MessageBox.Show(this,
                    $"После обрезки изображение ≈ {sizeKb} КБ (лимит {limitKb} КБ).\nСжать, чтобы уложиться?",
                    "Сжать аватар?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    MessageBox.Show(this,
                        $"Аватар должен быть ≤ {limitKb} КБ после обрезки.",
                        "Аватар", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!TryCompressToLimit(cropped, out prepared, out err))
                {
                    MessageBox.Show(this, err ?? $"Не удалось уложить аватар в {limitKb} КБ.", "Аватар",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            _avatarBytes = prepared;
            ApplyAvatarPreview(prepared);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load avatar file (best-effort)");
            MessageBox.Show(this, "Не удалось загрузить изображение.", "Аватар", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static bool TryCropTo512(ReadOnlySpan<byte> source,
        [NotNullWhen(true)] out byte[]? output,
        [NotNullWhen(false)] out string? error)
    {
        output = null;
        error = null;
        try
        {
            using var loaded = Img.Load(source);
            var side = Math.Min(loaded.Width, loaded.Height);
            if (side < 1)
            {
                error = "Некорректное изображение.";
                return false;
            }

            var cropX = (loaded.Width - side) / 2;
            var cropY = (loaded.Height - side) / 2;
            loaded.Mutate(x =>
            {
                x.Crop(new Rectangle(cropX, cropY, side, side));
                x.Resize(AvatarDimension, AvatarDimension);
            });

            using var ms = new MemoryStream();
            loaded.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
            output = ms.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryCompressToLimit(ReadOnlySpan<byte> source,
        [NotNullWhen(true)] out byte[]? output,
        [NotNullWhen(false)] out string? error)
    {
        output = null;
        error = null;
        try
        {
            using var loaded = Img.Load(source);
            for (var attempt = 0; attempt < 45; attempt++)
            {
                var q = Math.Max(22, Math.Min(88, 88 - attempt));
                var scale = attempt < 12 ? 1.0f : (float)Math.Pow(0.92, attempt - 11);
                using var work = loaded.Clone(x =>
                {
                    if (scale < 0.999f)
                    {
                        var w = Math.Max(48, (int)(AvatarDimension * scale));
                        var h = Math.Max(48, (int)(AvatarDimension * scale));
                        x.Resize(w, h);
                    }
                });
                using var ms = new MemoryStream();
                work.SaveAsJpeg(ms, new JpegEncoder { Quality = q });
                var bytes = ms.ToArray();
                if (bytes.Length <= PeerProfileLimits.MaxAvatarBytes)
                {
                    output = bytes;
                    return true;
                }
            }

            error = $"Не удалось уложить аватар в {PeerProfileLimits.MaxAvatarBytes / 1024} КБ.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void ApplyAvatarPreview(byte[]? bytes)
    {
        var previous = _avatarPreview.Image;
        _avatarPreview.Image = null;
        previous?.Dispose();
        if (bytes == null || bytes.Length == 0)
            return;
        try
        {
            using var ms = new MemoryStream(bytes, false);
            using var img = System.Drawing.Image.FromStream(ms);
            _avatarPreview.Image = new Bitmap(img);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preview avatar (best-effort)");
        }
    }

    private void SelectEconomy(TrafficQualityMode mode)
    {
        for (var i = 0; i < _economy.Items.Count; i++)
        {
            if (_economy.Items[i] is EconomyItem item && item.Mode == mode)
            {
                _economy.SelectedIndex = i;
                return;
            }
        }

        _economy.SelectedIndex = 0;
    }

    private void UpdateEconomyHint()
    {
        var mode = SelectedEconomy();
        var (w, h) = mode.GetVideoResolution();
        _economyHint.Text =
            $"голос {VoiceKbps(mode)} kbit/s  ·  видео {w}×{h}, {mode.GetCameraVideoBitrate() / 1000} kbit/s";
    }

    private static int VoiceKbps(TrafficQualityMode mode) =>
        mode switch
        {
            TrafficQualityMode.UltraEconomy => 8,
            TrafficQualityMode.Economy => 12,
            _ => 24
        };

    private TrafficQualityMode SelectedEconomy() =>
        _economy.SelectedItem is EconomyItem e ? e.Mode : TrafficQualityMode.Normal;

    private async Task SaveAsync()
    {
        try
        {
            if (_auth.CurrentUser != null)
            {
                var (ok, error) = await _auth.UpdateProfileAsync(_aboutMe.Text, _avatarBytes)
                    .ConfigureAwait(true);
                if (!ok)
                {
                    MessageBox.Show(this, error ?? "Ошибка сохранения профиля", "Профиль",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _ = BroadcastLocalUserInfoToContactsAsync();
            }

            var s = await _store.LoadAsync().ConfigureAwait(true);
            s.EnableUdpTransport = _lan.Checked;
            s.EnableBluetoothTransport = false;
            s.TrafficQuality = SelectedEconomy();
            s.MaxSearchHops = (int)_hops.Value;
            s.SendFailureSearchAttempts = (int)_attempts.Value;
            s.SendFailureRetryDelay = TimeSpan.FromMilliseconds((double)_delayMs.Value);
            s.SearchWaitTimeout = TimeSpan.FromMilliseconds((double)_timeoutMs.Value);
            if (_link.SelectedItem is LinkItem li)
                s.LinkTechnology = li.Preset;

            var cap = (s.AdvertisedPeerCapabilities & ~PresencePeerCapabilities.PeerSearch) |
                      PresencePeerCapabilities.Chat;
            if (_shareRoutes.Checked)
                cap |= PresencePeerCapabilities.PeerSearch;
            s.AdvertisedPeerCapabilities = cap;

            await _store.SaveAsync(s).ConfigureAwait(false);
            RoutingSettingsLive.Overlay(_live, s);
            _logger.LogInformation("Settings saved: udp={Udp} quality={Quality} hops={Hops}",
                s.EnableUdpTransport, s.TrafficQuality, s.MaxSearchHops);
            UpdateProfileSummary();
            MessageBox.Show(this, "Сохранено.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Save settings");
            MessageBox.Show(this, ex.Message, "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>TRL-7: push AboutMe + Avatar to every chat contact via messenger-server path (best-effort).</summary>
    private async Task BroadcastLocalUserInfoToContactsAsync()
    {
        var user = _auth.CurrentUser;
        if (user == null)
            return;

        try
        {
            var chats = await _chats.ListChatsAsync(user.Id).ConfigureAwait(false);
            foreach (var chat in chats)
            {
                try
                {
                    if (await _chats.IsPeerBlockedAsync(user.Id, chat.PeerNetworkIdShort).ConfigureAwait(false))
                        continue;

                    var session = _sessions.GetSession(
                        chat.Id,
                        () => new ChatP2PSession(chat, user, _chats, _sync, null, _logger),
                        s => s.ApplyChatRow(chat));
                    await session.SendLocalUserInfoAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "UserInfo broadcast to chat {ChatId} failed (best-effort)", chat.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UserInfo broadcast failed (best-effort)");
        }
    }

    private void CopyKeys()
    {
        var u = _auth.CurrentUser;
        if (u == null)
            return;
        try
        {
            var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
            var text = $"Network id: {u.NetworkIdShort}\nPublic key JSON:\n{pub}";
            Clipboard.SetText(text);
            MessageBox.Show(this, "Ключи скопированы в буфер обмена.", "Настройки", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copy keys");
            MessageBox.Show(this, ex.Message, "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    private static decimal Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private static string FormatStorage(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return "—";
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
            var mb = bytes / (1024.0 * 1024.0);
            return mb >= 1024 ? $"{mb / 1024:0.00} ГБ  ({dir})" : $"{mb:0.00} МБ  ({dir})";
        }
        catch
        {
            return "—";
        }
    }

    private static string TrimAbout(string text, int max)
    {
        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private sealed record EconomyItem(TrafficQualityMode Mode)
    {
        public override string ToString() => Mode switch
        {
            TrafficQualityMode.UltraEconomy => "Ультраэкономия (144p / 8 kbit/s голос)",
            TrafficQualityMode.Economy => "Экономия (240p / 12 kbit/s голос)",
            _ => "Нормальный (480p / 24 kbit/s голос)"
        };
    }

    private sealed record LinkItem(LinkTechnologyPreset Preset)
    {
        public override string ToString() => Preset.GetDisplayLabel();
    }
}
