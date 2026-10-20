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

/// <summary>Einrichtungscodes aus dem IT Center (10, AK-4) und weitere Anmeldungen auf Poolgeräten (11).</summary>
public sealed class EnrollmentAndAttachTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres,
            new Dictionary<string, string> { ["EstrichOne:PublicUrl"] = "https://estrichone.example.com/" });
        _admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task EnrollmentCode_ApprovesDeviceWithKindAndName_FromAdministrator()
    {
        var code = await CreateCodeAsync("pool", "Fahrzeug 3");
        code.Link.ShouldNotBeNull();
        code.Link.ShouldStartWith("estrichone://enroll?v=1&server=https%3A%2F%2Festrichone.example.com&");
        code.Link.ShouldContain($"code={code.Code}&type=pool");
        code.ExpiresAt.ShouldBe(_factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(15));

        var phone = await LoginPhoneAsync("baustelle");
        using var response = await phone.RegisterAsync(kind: "personal", enrollmentCode: code.Code);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var device = (await response.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;
        device.Kind.ShouldBe(DeviceKind.Pool);
        device.Name.ShouldBe("Fahrzeug 3");
        device.Status.ShouldBe(DeviceStatus.Active);
        (await phone.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnrollmentCode_IsAcceptedWithoutHyphenAndInLowercase()
    {
        var code = await CreateCodeAsync("personal");
        var phone = await LoginPhoneAsync("baustelle");

        using var response = await phone.RegisterAsync(kind: "personal", enrollmentCode: code.Code.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task EnrollmentCode_CanBeUsedOnlyOnce()
    {
        var code = await CreateCodeAsync("pool");
        var first = await LoginPhoneAsync("erster");
        var second = await LoginPhoneAsync("zweiter");

        (await first.RegisterAsync("pool", code.Code)).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var again = await second.RegisterAsync("pool", code.Code);

        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await again.ReadProblemAsync()).Code.ShouldBe("ENROLLMENT_CODE_INVALID");
        // Die abgelehnte Registrierung legt kein Gerät an und bindet die Sitzung nicht.
        (await second.Client.GetAsync(new Uri("/api/v1/devices/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnrollmentCode_ParallelRedemption_SucceedsExactlyOnce()
    {
        var code = await CreateCodeAsync("pool");
        var phones = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => LoginPhoneAsync($"parallel{i}")));

        var responses = await Task.WhenAll(phones.Select(phone => phone.RegisterAsync("pool", code.Code)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(4);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task EnrollmentCode_ExpiresAfterFifteenMinutes()
    {
        var code = await CreateCodeAsync("personal");
        _factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var phone = await LoginPhoneAsync("baustelle");

        using var response = await phone.RegisterAsync("personal", code.Code);

        (await response.ReadProblemAsync()).Code.ShouldBe("ENROLLMENT_CODE_INVALID");
    }

    [Fact]
    public async Task UnknownEnrollmentCode_IsRejected()
    {
        var phone = await LoginPhoneAsync("baustelle");

        using var response = await phone.RegisterAsync("pool", "AAAA-BBBB");

        (await response.ReadProblemAsync()).Code.ShouldBe("ENROLLMENT_CODE_INVALID");
    }

    [Fact]
    public async Task CreatingEnrollmentCodes_RequiresDeviceManagement()
    {
        using var worker = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);

        using var response = await worker.PostAsJsonAsync("/api/v1/devices/enrollment-codes", new { kind = "pool" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SecondUser_OnPoolDevice_AttachesWithDeviceSignature()
    {
        var code = await CreateCodeAsync("pool", "Fahrzeug 3");
        var first = await LoginPhoneAsync("erster");
        using var registered = await first.RegisterAsync("pool", code.Code);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;

        // Zweite Person meldet sich auf demselben Gerät an: neue Sitzung, gleicher Geräteschlüssel.
        var second = await LoginPhoneAsync("zweiter", first.Key);
        (await second.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var attached = await second.AttachAsync(device.Id);

        attached.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Die Sitzung ist jetzt gerätegebunden: Erneuern nur mit Signatur.
        using var unsigned = await second.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.Tokens.RefreshToken }, Ct);
        (await unsigned.ReadProblemAsync()).Code.ShouldBe("DEVICE_PROOF_INVALID");
    }

    [Fact]
    public async Task Attach_WithForeignKeyOrReplayedSignature_IsRejected()
    {
        var first = await LoginPhoneAsync("erster");
        using var registered = await first.RegisterAsync("pool", (await CreateCodeAsync("pool")).Code);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var thief = await LoginPhoneAsync("dieb", otherKey);
        using var forged = await thief.AttachAsync(device.Id);
        forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forged.ReadProblemAsync()).Code.ShouldBe("DEVICE_PROOF_INVALID");

        // Eine mitgeschnittene Signatur für eine andere Sitzung nützt nichts.
        var victim = await LoginPhoneAsync("opfer", first.Key);
        var replayed = Sign(first.Key, Device.AttachProofPrefix + victim.Tokens.AccessToken);
        using var replay = await thief.Client.PostAsJsonAsync("/api/v1/devices/attach", new { deviceId = device.Id, signature = replayed }, Ct);
        (await replay.ReadProblemAsync()).Code.ShouldBe("DEVICE_PROOF_INVALID");
    }

    [Fact]
    public async Task Attach_ToPersonalDeviceOfSomeoneElse_IsRejected()
    {
        var owner = await LoginPhoneAsync("besitzer");
        using var registered = await owner.RegisterAsync("personal", enrollmentCode: null);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;

        var other = await LoginPhoneAsync("andere", owner.Key);
        using var response = await other.AttachAsync(device.Id);

        (await response.ReadProblemAsync()).Code.ShouldBe("DEVICE_NOT_ASSIGNED");

        // Die Besitzerin selbst kann nach erneuter Anmeldung wieder anbinden.
        var again = await LoginPhoneAsync("besitzer", owner.Key, createUser: false);
        (await again.AttachAsync(device.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Attach_ToRetiredDevice_IsRejected()
    {
        var first = await LoginPhoneAsync("erster");
        using var registered = await first.RegisterAsync("pool", (await CreateCodeAsync("pool")).Code);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;
        (await _admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/retire", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();

        var second = await LoginPhoneAsync("zweiter", first.Key);
        using var response = await second.AttachAsync(device.Id);

        (await response.ReadProblemAsync()).Code.ShouldBe("DEVICE_NOT_FOUND");
    }

    private async Task<EnrollmentCodeResponse> CreateCodeAsync(string kind, string? deviceName = null)
    {
        using var response = await _admin.PostAsJsonAsync("/api/v1/devices/enrollment-codes", new { kind, deviceName }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EnrollmentCodeResponse>(TestJson.Options, Ct))!;
    }

    private async Task<Phone> LoginPhoneAsync(string userName, ECDsa? key = null, bool createUser = true)
    {
        if (createUser)
        {
            await _factory.CreateUserAsync(userName, roleIds: StandardRoles.SiteWorkerId);
        }

        var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync(userName, clientKind: "ios");
        return new Phone(client.WithToken(tokens.AccessToken), tokens, key ?? ECDsa.Create(ECCurve.NamedCurves.nistP256));
    }

    private static string Sign(ECDsa key, string message) =>
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256));

    private sealed record Phone(HttpClient Client, TokenResponse Tokens, ECDsa Key)
    {
        public Task<HttpResponseMessage> RegisterAsync(string kind, string? enrollmentCode) =>
            Client.PostAsJsonAsync("/api/v1/devices/register", new
            {
                name = "iPhone",
                platform = "ios",
                kind,
                publicKey = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo()),
                enrollmentCode,
            });

        public Task<HttpResponseMessage> AttachAsync(Guid deviceId) =>
            Client.PostAsJsonAsync("/api/v1/devices/attach", new
            {
                deviceId,
                signature = Sign(Key, Device.AttachProofPrefix + Tokens.AccessToken),
            });
    }
}
