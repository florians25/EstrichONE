using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Devices;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Organization;
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

    [Fact]
    public async Task LostRefreshResponse_OfDevice_IsRecovered_InsteadOfEndingTheSession()
    {
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        // Antwort geht verloren (Funkloch, App im Hintergrund beendet): Der Server hat rotiert, die App kennt nur T0.
        using var lost = await RefreshAsync(phone, phone.Tokens.RefreshToken);
        lost.StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Clock.Advance(TimeSpan.FromHours(6));
        using var retry = await RefreshAsync(phone, phone.Tokens.RefreshToken);

        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recovered = (await retry.Content.ReadFromJsonAsync<RefreshResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        using var me = await _factory.CreateClient().WithToken(recovered.AccessToken)
            .GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Der verlorene Nachfolger ist verworfen; weiter geht es mit dem neuen Token.
        using var next = await RefreshAsync(phone, recovered.RefreshToken);
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReusedRefreshToken_WhoseSuccessorWasUsed_StillEndsTheDeviceSession()
    {
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        using var first = await RefreshAsync(phone, phone.Tokens.RefreshToken);
        var t1 = (await first.Content.ReadFromJsonAsync<RefreshResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        using var second = await RefreshAsync(phone, t1.RefreshToken);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var replay = await RefreshAsync(phone, phone.Tokens.RefreshToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await replay.ReadProblemAsync()).Code.ShouldBe("REFRESH_TOKEN_REUSED");
    }

    [Fact]
    public async Task DeviceSession_IsSliding_EndsOnlyAfterConfiguredIdleDays()
    {
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        var token = phone.Tokens.RefreshToken;
        // 4 × 80 Tage Nutzung: weit über die frühere feste Höchstdauer (90 Tage) hinaus angemeldet.
        for (var i = 0; i < 4; i++)
        {
            _factory.Clock.Advance(TimeSpan.FromDays(80));
            using var response = await RefreshAsync(phone, token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var refreshed = (await response.Content.ReadFromJsonAsync<RefreshResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
            refreshed.RefreshTokenExpiresAt.ShouldBe(_factory.Clock.GetUtcNow().AddDays(90), TimeSpan.FromSeconds(1));
            token = refreshed.RefreshToken;
        }

        _factory.Clock.Advance(TimeSpan.FromDays(91));
        using var idle = await RefreshAsync(phone, token);
        (await idle.ReadProblemAsync()).Code.ShouldBe("SESSION_EXPIRED");
    }

    [Fact]
    public async Task DeviceIdleDays_ComeFromSettings()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var settings = (await admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, TestContext.Current.CancellationToken))!;
        settings.Settings.Session.DeviceIdleDays.ShouldBe(90);
        var shorter = settings.Settings with { Session = settings.Settings.Session with { DeviceIdleDays = 10 } };
        (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = shorter, version = settings.Version }, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        phone.Tokens.RefreshTokenExpiresAt.ShouldBe(_factory.Clock.GetUtcNow().AddDays(10), TimeSpan.FromSeconds(5));

        _factory.Clock.Advance(TimeSpan.FromDays(11));
        using var idle = await RefreshAsync(phone, phone.Tokens.RefreshToken);
        (await idle.ReadProblemAsync()).Code.ShouldBe("SESSION_EXPIRED");
    }

    [Fact]
    public async Task EndedDeviceSession_TellsWhy()
    {
        var phone = await LoginPhoneAsync("baustelle");
        await phone.RegisterAsync(DeviceKind.Personal);
        using var web = _factory.CreateClient();
        var office = await web.LoginOkAsync("baustelle");
        (await web.WithToken(office.AccessToken).PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = AuthClient.DefaultPassword, newPassword = "Neue Passphrase 2026" }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refresh = await RefreshAsync(phone, phone.Tokens.RefreshToken);

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await refresh.ReadProblemAsync()).Code.ShouldBe("PASSWORD_CHANGED");
    }

    [Fact]
    public async Task ConcurrentRefresh_WithoutDevice_IsConflict_NotLogout()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("buero", clientKind: "desktop");
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();

        using var parallel = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, TestContext.Current.CancellationToken);

        parallel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await parallel.ReadProblemAsync()).Code.ShouldBe("REFRESH_IN_PROGRESS");
    }

    private Task<HttpResponseMessage> RefreshAsync(Phone phone, string refreshToken) =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken, deviceSignature = Sign(phone.Key, refreshToken) }, TestContext.Current.CancellationToken);

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
