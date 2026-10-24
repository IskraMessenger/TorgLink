using ShortP2P.Auth;
using ShortP2P.Client.Services;
using ShortP2P.Transport;
using TorgLink.Localization;

namespace TorgLink.Maui;

public partial class NetworkPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly IBluetoothRadioCatalog _bluetoothCatalog;
    private readonly UserP2pRuntime _p2p;

    public NetworkPage(AuthService auth, UserP2pRuntime p2p, IBluetoothRadioCatalog bluetoothCatalog)
    {
        InitializeComponent();
        _auth = auth;
        _p2p = p2p;
        _bluetoothCatalog = bluetoothCatalog;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Title = LocalizationUtils.GetStringByKey("tab.network");
        ApplyLocalizedUi();
        Header.Bind(_auth.CurrentUser, _p2p);
    }

    private void ApplyLocalizedUi()
    {
        AddChatButton.Text = LocalizationUtils.GetStringByKey("network.add_chat");
        MyQrButton.Text = LocalizationUtils.GetStringByKey("network.my_qr");
        MyAddressesButton.Text = LocalizationUtils.GetStringByKey("network.my_addresses");
        CopyKeyButton.Text = LocalizationUtils.GetStringByKey("network.copy_key");
        LanScanButton.Text = LocalizationUtils.GetStringByKey("settings.lan");
        RoutingButton.Text = LocalizationUtils.GetStringByKey("settings.routing");
        ServersButton.Text = LocalizationUtils.GetStringByKey("network.servers");
    }

    private async void OnAddChatClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<AddChatPage>();
        await Navigation.PushModalAsync(new NavigationPage(page)).ConfigureAwait(true);
    }

    private async void OnMyQrClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(MauiProgram.Services.GetRequiredService<MyQrPage>()).ConfigureAwait(true);

    private async void OnMyAddressesClicked(object? sender, EventArgs e) =>
        await ProfileShare.CopyAddressesAsync(this, _auth, _p2p, _bluetoothCatalog).ConfigureAwait(true);

    private async void OnCopyKeysClicked(object? sender, EventArgs e) =>
        await ProfileShare.CopyKeysAsync(this, _auth).ConfigureAwait(true);

    private async void OnLanScanClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(MauiProgram.Services.GetRequiredService<LanScanPage>()).ConfigureAwait(true);

    private async void OnRoutingClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(MauiProgram.Services.GetRequiredService<RoutingSettingsPage>()).ConfigureAwait(true);

    private async void OnServersClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(MauiProgram.Services.GetRequiredService<MessengerServersPage>()).ConfigureAwait(true);
}

