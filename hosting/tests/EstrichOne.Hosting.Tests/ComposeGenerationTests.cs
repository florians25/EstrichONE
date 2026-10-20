using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;

namespace EstrichOne.Hosting.Tests;

public sealed class ComposeGenerationTests
{
    private static Tenant Tenant() => new()
    {
        Slug = "mueller",
        CompanyName = "Estrichbau Müller GmbH",
        Domain = "mueller.hosting.de",
        Version = "0.1.0-preview.4",
    };

    private static readonly Plan Plan = new() { Name = "Business", MaxUsers = 20, StorageGb = 100, MemoryMb = 2048, CpuLimit = 1.5m };

    [Fact]
    public void BuildEnv_SetztBetriebsartProxyUndAlleWerte()
    {
        var env = InstanceFiles.ParseEnv(InstanceFiles.BuildEnv(Tenant(), "pg-geheim", "restic-geheim", new HostingOptions()));
        env["COMPOSE_PROJECT_NAME"].ShouldBe("estrichone-mueller");
        env["COMPOSE_FILE"].ShouldBe("compose.yml:compose.override.yml:compose.proxy.yml:compose.hosting.yml");
        env["ESTRICHONE_MODE"].ShouldBe("proxy");
        env["ESTRICHONE_VERSION"].ShouldBe("0.1.0-preview.4");
        env["ESTRICHONE_REGISTRY"].ShouldBe("ghcr.io/florians25");
        env["ESTRICHONE_DOMAIN"].ShouldBe("mueller.hosting.de");
        env["ESTRICHONE_PUBLIC_URL"].ShouldBe("https://mueller.hosting.de");
        env["ESTRICHONE_SERVER_NAME"].ShouldBe("Estrichbau Müller GmbH");
        env["ESTRICHONE_HTTP_PORT"].ShouldBe("8181");
        env["ESTRICHONE_TRUSTED_PROXIES"].ShouldBe("private_ranges");
        env["POSTGRES_PASSWORD"].ShouldBe("pg-geheim");
        env["RESTIC_PASSWORD"].ShouldBe("restic-geheim");
        env["ESTRICHONE_BACKUP_DIR"].ShouldBe("./backup-repository");
        env["BACKUP_TIME"].ShouldMatch("^([01][0-9]|2[0-3]):[0-5][0-9]$");
    }

    [Fact]
    public void BuildEnv_OhneTls_VerwendetHttp() =>
        InstanceFiles.ParseEnv(InstanceFiles.BuildEnv(Tenant(), "a", "b", new HostingOptions { Tls = "off" }))["ESTRICHONE_PUBLIC_URL"]
            .ShouldBe("http://mueller.hosting.de");

    [Fact]
    public void BuildEnv_MitBackupRoot_LegtRepositoryDortAb() =>
        InstanceFiles.ParseEnv(InstanceFiles.BuildEnv(Tenant(), "a", "b", new HostingOptions { BackupRoot = "/mnt/backup" }))["ESTRICHONE_BACKUP_DIR"]
            .ShouldBe("/mnt/backup/mueller");

    [Fact]
    public void Secret_IstZufaelligUndAlphanumerisch()
    {
        var a = InstanceFiles.Secret(40);
        a.Length.ShouldBe(40);
        a.ShouldMatch("^[A-Za-z0-9]+$");
        InstanceFiles.Secret(40).ShouldNotBe(a);
    }

    [Fact]
    public void BackupTime_LiegtNachtsUndIstStabil()
    {
        foreach (var slug in new[] { "abc", "mueller", "estrichbau-nord", "firma-2" })
        {
            var time = InstanceFiles.BackupTime(slug);
            InstanceFiles.BackupTime(slug).ShouldBe(time);
            var hour = int.Parse(time[..2], System.Globalization.CultureInfo.InvariantCulture);
            (hour >= 21 || hour < 5).ShouldBeTrue(time);
        }
    }

    [Fact]
    public void BuildOverlay_OhnePortsMitEdgeNetzUndGrenzen()
    {
        var overlay = InstanceFiles.BuildOverlay("mueller", Plan);
        overlay.ShouldContain("ports: !reset []");
        overlay.ShouldContain("networks: !override");
        overlay.ShouldContain("name: estrichone-mueller-edge");
        overlay.ShouldContain("external: true");
        overlay.ShouldContain("mem_limit: 2048m");
        overlay.ShouldContain("cpus: 1.5");
        overlay.ShouldContain("de.estrichone.hosting.tenant: \"mueller\"");
    }

    [Fact]
    public void BuildOverlay_OhneGrenzen_LaesstSieWeg()
    {
        var overlay = InstanceFiles.BuildOverlay("mueller", new Plan { Name = "Frei" });
        overlay.ShouldNotContain("mem_limit");
        overlay.ShouldNotContain("cpus:");
    }

    [Fact]
    public void ParseEnv_EntferntAnfuehrungszeichenUndKommentare()
    {
        var env = InstanceFiles.ParseEnv("# Kommentar\nA=1\nB=\"Firma GmbH\"\r\nC=\nkaputt\n");
        env["A"].ShouldBe("1");
        env["B"].ShouldBe("Firma GmbH");
        env["C"].ShouldBe(string.Empty);
        env.ShouldNotContainKey("kaputt");
    }

    [Fact]
    public void ComposeArguments_UebergibtProjektDateienUndEnvAlsEinzelneArgumente()
    {
        var project = new ComposeProject("estrichone-mueller", "/srv/h/instances/mueller", InstanceLayout.ComposeFiles);
        var args = DockerCliHost.ComposeArguments(project, ["up", "-d"]);
        args.ShouldBe(new[]
        {
            "compose", "--project-directory", "/srv/h/instances/mueller", "-p", "estrichone-mueller",
            "-f", "/srv/h/instances/mueller/compose.yml", "-f", "/srv/h/instances/mueller/compose.override.yml",
            "-f", "/srv/h/instances/mueller/compose.proxy.yml", "-f", "/srv/h/instances/mueller/compose.hosting.yml",
            "--env-file", "/srv/h/instances/mueller/.env", "up", "-d",
        });
    }

    [Fact]
    public void InstanceLayout_WeistUngueltigeKuerzelAb()
    {
        Should.Throw<ArgumentException>(() => InstanceLayout.ProjectName("../x"));
        Should.Throw<ArgumentException>(() => InstanceLayout.EdgeNetwork("a b"));
        InstanceLayout.WebContainer("mueller").ShouldBe("estrichone-mueller-caddy-1");
    }

    [Fact]
    public async Task InstanceFiles_PrepareUndUpdate_BehaltenPasswoerter()
    {
        using var temp = new TempDirectory();
        var options = Microsoft.Extensions.Options.Options.Create(new HostingOptions
        {
            Root = temp.Path,
            TemplateDirectory = TestPaths.Templates,
            Docker = "fake",
        });
        var layout = new InstanceLayout(options);
        var files = new InstanceFiles(options, layout, Microsoft.Extensions.Logging.Abstractions.NullLogger<InstanceFiles>.Instance);
        var tenant = Tenant();
        (await files.PrepareAsync(tenant, Plan, CancellationToken.None)).ShouldBeTrue();
        var first = await files.ReadEnvAsync("mueller", CancellationToken.None);
        File.Exists(Path.Combine(temp.Path, "instances", "mueller", "compose.yml")).ShouldBeTrue();
        File.Exists(Path.Combine(temp.Path, "instances", "mueller", "compose.hosting.yml")).ShouldBeTrue();
        Directory.Exists(Path.Combine(temp.Path, "instances", "mueller", "data", "backup-status")).ShouldBeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(temp.Path, "instances", "mueller", ".env"))
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        tenant.Version = "0.1.0-preview.5";
        tenant.Domain = "neu.hosting.de";
        (await files.PrepareAsync(tenant, Plan, CancellationToken.None)).ShouldBeFalse();
        var second = await files.ReadEnvAsync("mueller", CancellationToken.None);
        second["POSTGRES_PASSWORD"].ShouldBe(first["POSTGRES_PASSWORD"]);
        second["RESTIC_PASSWORD"].ShouldBe(first["RESTIC_PASSWORD"]);
        second["ESTRICHONE_VERSION"].ShouldBe("0.1.0-preview.5");
        second["ESTRICHONE_PUBLIC_URL"].ShouldBe("https://neu.hosting.de");
    }
}
