using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Devices;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Devices;

public sealed class DeviceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task MobileSession_WithoutDevice_GetsNoBusinessData()
    {
        var phone = await LoginPhoneAsync("baustelle");

        using var response = await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code.ShouldBe("DEVICE_NOT_APPROVED");
    }

    [Fact]
    public async Task PersonalDevice_WithSelfEnrollPermission_IsApprovedImmediately()
    {
        var phone = await LoginPhoneAsync("baustelle");

        var device = await phone.RegisterAsync(DeviceKind.Personal);

        device.Status.ShouldBe(DeviceStatus.Active);
        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PoolDevice_WaitsForAdministrator()
    {
        var phone = await LoginPhoneAsync("baustelle");
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);

        var device = await phone.RegisterAsync(DeviceKind.Pool);
        device.Status.ShouldBe(DeviceStatus.Pending);
        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await phone.Client.GetFromJsonAsync<DeviceResponse>("/api/v1/devices/me", TestJson.Options, TestContext.Current.CancellationToken))!.Status.ShouldBe(DeviceStatus.Pending);

        (await admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/approve", UriKind.Relative), null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WithInvalidKey_OrTwice_IsRejected()
    {
        var phone = await LoginPhoneAsync("baustelle");

        using var invalid = await phone.Client.PostAsJsonAsync("/api/v1/devices/register",
            new { name = "iPhone", platform = "ios", kind = "personal", publicKey = Convert.ToBase64String(new byte[65]) }, TestContext.Current.CancellationToken);
        (await invalid.ReadProblemAsync()).Code.ShouldBe("INVALID_DEVICE_KEY");

        await phone.RegisterAsync(DeviceKind.Personal);
        using var again = await phone.Client.PostAsJsonAsync("/api/v1/devices/register", phone.RegistrationBody(DeviceKind.Personal), TestContext.Current.CancellationToken);
        (await again.ReadProblemAsync()).Code.ShouldBe("DEVICE_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Refresh_OfDeviceSession_RequiresSignatureOfThatDevice()
    {
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        using var thief = _factory.CreateClient();

        using var unsigned = await thief.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = phone.Tokens.RefreshToken }, TestContext.Current.CancellationToken);
        (await unsigned.ReadProblemAsync()).Code.ShouldBe("DEVICE_PROOF_INVALID");

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var forged = await thief.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = phone.Tokens.RefreshToken,
            deviceSignature = Sign(otherKey, phone.Tokens.RefreshToken),
        }, TestContext.Current.CancellationToken);
        (await forged.ReadProblemAsync()).Code.ShouldBe("DEVICE_PROOF_INVALID");

        using var signed = await phone.Client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = phone.Tokens.RefreshToken,
            deviceSignature = Sign(phone.Key, phone.Tokens.RefreshToken),
        }, TestContext.Current.CancellationToken);
        signed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LockedDevice_LosesAccess_RetiredDeviceLosesSession()
    {
        var phone = await LoginPhoneAsync("baustelle");
        var device = await phone.RegisterAsync(DeviceKind.Personal);
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);

        (await admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/lock", UriKind.Relative), null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var locked = await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await locked.ReadProblemAsync()).Code.ShouldBe("DEVICE_LOCKED");

        (await admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/unlock", UriKind.Relative), null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/retire", UriKind.Relative), null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await phone.Client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = phone.Tokens.RefreshToken,
            deviceSignature = Sign(phone.Key, phone.Tokens.RefreshToken),
        }, TestContext.Current.CancellationToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RetiredDevice_CanRegisterAgain_WithSameKeyAndName_OldEntryStaysRetired()
    {
        var phone = await LoginPhoneAsync("baustelle");
        var old = await phone.RegisterAsync(DeviceKind.Personal);
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        (await admin.PostAsync(new Uri($"/api/v1/devices/{old.Id}/retire", UriKind.Relative), null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        // Neu anmelden (die alte Sitzung ist beendet) und mit demselben Schlüssel und Namen erneut registrieren.
        using var fresh = _factory.CreateClient();
        var tokens = await fresh.LoginOkAsync("baustelle", clientKind: "ios");
        var again = phone with { Client = fresh.WithToken(tokens.AccessToken), Tokens = tokens };
        var device = await again.RegisterAsync(DeviceKind.Personal);

        device.Id.ShouldNotBe(old.Id);
        device.Status.ShouldBe(DeviceStatus.Active);
        (await again.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var all = (await admin.GetFromJsonAsync<List<DeviceResponse>>("/api/v1/devices", TestJson.Options, TestContext.Current.CancellationToken))!;
        all.Single(d => d.Id == old.Id).Status.ShouldBe(DeviceStatus.Retired);
        all.Single(d => d.Id == device.Id).Status.ShouldBe(DeviceStatus.Active);
    }

    [Fact]
    public async Task DeviceAdministration_RequiresPermission()
    {
        var phone = await LoginPhoneAsync("baustelle");
        var device = await phone.RegisterAsync(DeviceKind.Pool);
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);

        using var response = await colleague.PostAsync(new Uri($"/api/v1/devices/{device.Id}/approve", UriKind.Relative), null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<Phone> LoginPhoneAsync(string userName)
    {
        await _factory.CreateUserAsync(userName, roleIds: StandardRoles.SiteWorkerId);
        var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync(userName, clientKind: "ios");
        return new Phone(client.WithToken(tokens.AccessToken), tokens, ECDsa.Create(ECCurve.NamedCurves.nistP256));
    }

    private static string Sign(ECDsa key, string refreshToken) =>
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(Device.RefreshProofPrefix + refreshToken), HashAlgorithmName.SHA256));

    private sealed record Phone(HttpClient Client, TokenResponse Tokens, ECDsa Key)
    {
        public object RegistrationBody(DeviceKind kind) => new
        {
            name = "iPhone von Max",
            platform = "ios",
            kind = kind == DeviceKind.Pool ? "pool" : "personal",
            publicKey = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()),
            model = "iPhone16,2",
            osVersion = "26.1",
            appVersion = "0.1.0",
        };

        public async Task<DeviceResponse> RegisterAsync(DeviceKind kind)
        {
            using var response = await Client.PostAsJsonAsync("/api/v1/devices/register", RegistrationBody(kind));
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options))!;
        }
    }
}
