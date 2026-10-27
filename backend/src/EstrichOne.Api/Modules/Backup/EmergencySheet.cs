using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// Notfallblatt zum Ausdrucken: alles, um nach einem Totalverlust des Servers auf neuer Hardware wiederherzustellen –
/// Wiederherstellungsschlüssel (Repository-Passwort), Sicherungsziele mit Zugang und Fingerabdruck, der eine Befehl.
/// </summary>
internal sealed class EmergencySheetService(
    IPdfBranding branding, BackupTargetService targets, BackupMirrorStore mirrors, IOptions<BackupSettings> settings,
    IOptions<EstrichOneOptions> options, TimeProvider clock)
{
    public async Task<byte[]> RenderAsync(string serverUrl, CancellationToken cancellationToken)
    {
        var company = await branding.GetAsync(cancellationToken);
        var s = settings.Value;
        var (document, section) = PdfReport.Create("Notfallblatt Datensicherung", "Wiederherstellung von EstrichONE nach Totalverlust", company);
        section.AddParagraph("Notfallblatt Datensicherung").Style = StyleNames.Heading1;
        var intro = section.AddParagraph("VERTRAULICH – ausgedruckt getrennt vom Server aufbewahren (Tresor, Bankschließfach, beim Geschäftsführer). "
            + "Wer dieses Blatt hat, kann die Sicherungen entschlüsseln. Ohne den Wiederherstellungsschlüssel ist keine Wiederherstellung möglich.");
        intro.Format.Font.Bold = true;
        intro.Format.SpaceAfter = Unit.FromMillimeter(3);

        PdfReport.AddFacts(section, 17,
            ("Betrieb", company.Name), ("Server", serverUrl), ("Servername", options.Value.ServerName),
            ("Version", ApplicationVersion.Current), ("Erstellt", PdfReport.DateTime(clock.GetUtcNow())));

        section.AddParagraph("Wiederherstellungsschlüssel (Repository-Passwort)").Style = StyleNames.Heading2;
        var key = section.AddParagraph(string.IsNullOrWhiteSpace(s.RepositoryPassword)
            ? "Nicht hinterlegt – steht als RESTIC_PASSWORD in der Datei .env des Servers (Installationsverzeichnis)."
            : Group(s.RepositoryPassword));
        key.Format.Font.Size = string.IsNullOrWhiteSpace(s.RepositoryPassword) ? 9.5 : 14;
        key.Format.Font.Bold = !string.IsNullOrWhiteSpace(s.RepositoryPassword);
        key.Format.Shading.Color = PdfReport.Shade;
        key.Format.Borders.Distance = Unit.FromMillimeter(2);
        key.Format.SpaceAfter = Unit.FromMillimeter(1);
        if (!string.IsNullOrWhiteSpace(s.RepositoryPassword))
        {
            section.AddParagraph("Leerzeichen dienen nur der Lesbarkeit und werden nicht mit eingegeben.").Style = "Muted";
        }

        section.AddParagraph("Sicherungsziele").Style = StyleNames.Heading2;
        var table = PdfReport.AddTable(section, ("Ziel", 4.2, ParagraphAlignment.Left), ("Zugang für die Wiederherstellung", 12.8, ParagraphAlignment.Left));
        if (s.Repository is { Length: > 0 } primary)
        {
            PdfReport.AddRow(table, "Hauptziel", primary == "/repository"
                ? $"Ordner auf dem Server bzw. der eingebundenen Platte: {s.HostBackupDir ?? "./backup-repository"} (im Installationsverzeichnis). "
                  + "Nach Totalverlust nur nutzbar, wenn diese Platte erhalten ist – dann den Ordner auf den neuen Server kopieren."
                : primary);
        }

        if (s.Repository2 is { Length: > 0 } second)
        {
            PdfReport.AddRow(table, "Zweites Ziel", second);
        }

        foreach (var (name, address, repository, fingerprint) in await targets.RecoveryEntriesAsync(cancellationToken))
        {
            PdfReport.AddRow(table, $"Backup-Ziel „{name}“", $"{repository}\nFingerabdruck: {fingerprint}\nAdresse: {address}");
        }

        foreach (var mirror in mirrors.List().Where(m => m.Paired))
        {
            PdfReport.AddRow(table, $"Backup-Ziel „{mirror.TargetName ?? mirror.Name}“ (zieht)",
                "Spiegel auf dem Rechner zuhause. Dort den Ordner sichern: sudo docker cp estrichone-backup-target:/data/mirror ./estrichone-backup "
                + "und auf den neuen Server kopieren (USB-Platte, scp). Dann als Quelle den Ordnerpfad angeben.");
        }

        section.AddParagraph("Wiederherstellen auf einem neuen Server").Style = StyleNames.Heading2;
        var version = ApplicationVersion.Current.Split('+')[0];
        foreach (var (n, step) in new[]
        {
            (1, "Neuen Linux-Server mit Docker bereitstellen (gleiche Domain bzw. DNS-Eintrag auf die neue IP umstellen)."),
            (2, $"Diesen Befehl ausführen:\nsudo mkdir -p /opt/estrichone && sudo docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone:/opt/estrichone -v /mnt:/mnt {s.InstallerImage}:{version} restore"),
            (3, "Der Installer fragt nach der Quelle (Ordnerpfad, z. B. /mnt/usb/estrichone-backup, oder ein Zugang aus der Tabelle oben), dem Fingerabdruck "
                + "(nur bei Backup-Zielen) und dem Wiederherstellungsschlüssel. Er holt die letzte Sicherung, stellt Datenbank, Dateien, Fotos, Dokumente, "
                + "Einstellungen und Schlüssel wieder her und startet EstrichONE."),
            (4, "Anmelden und im Backup Center prüfen: Sicherungsziele erneut koppeln, falls die Adresse sich geändert hat. Apps gleichen automatisch neu ab."),
        })
        {
            var p = section.AddParagraph($"{n}.\t{step}");
            p.Format.LeftIndent = Unit.FromMillimeter(6);
            p.Format.FirstLineIndent = Unit.FromMillimeter(-6);
            p.Format.TabStops.AddTabStop(Unit.FromMillimeter(6));
            p.Format.SpaceAfter = Unit.FromMillimeter(1.5);
        }

        section.AddParagraph("Ausführliche Anleitung: docs/backup.md im EstrichONE-Repository (Abschnitt „Worst Case“).").Style = "Muted";
        return PdfReport.Render(document);
    }

    /// <summary>Viererblöcke für das Abschreiben von Hand.</summary>
    internal static string Group(string value) =>
        string.Join(' ', Enumerable.Range(0, (value.Length + 3) / 4).Select(i => value.Substring(i * 4, Math.Min(4, value.Length - (i * 4)))));
}
