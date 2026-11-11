using EstrichOne.Api.Modules.Helpdesk;

namespace EstrichOne.Api.Tests.Helpdesk;

/// <summary>SLA in Geschäftszeit, automatische Zuweisung und Artikelsuche – reine Logik ohne Datenbank.</summary>
public sealed class SlaAndAssignmentTests
{
    private static readonly BusinessHours Hours = new("07:00", "17:00");
    private static readonly HashSet<DateOnly> NoHolidays = [];

    /// <summary>Ortszeit Berlin als Zeitpunkt (Winterzeit +01:00 bzw. Sommerzeit +02:00).</summary>
    private static DateTimeOffset Berlin(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, SlaCalculator.Berlin.GetUtcOffset(local));
    }

    [Fact]
    public void AddBusinessTime_WithinTheSameDay()
    {
        // Montag, 13. Januar 2031, 09:00 + 4 h → 13:00
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 13, 9), TimeSpan.FromHours(4), Hours, NoHolidays).ShouldBe(Berlin(2031, 1, 13, 13));
    }

    [Fact]
    public void AddBusinessTime_ContinuesNextMorning_AndSkipsWeekend()
    {
        // Montag 15:00 + 4 h: 2 h bis 17:00, Rest ab Dienstag 07:00 → 09:00
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 13, 15), TimeSpan.FromHours(4), Hours, NoHolidays).ShouldBe(Berlin(2031, 1, 14, 9));
        // Freitag 16:00 + 8 h: 1 h Freitag, 7 h Montag → Montag 14:00
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 17, 16), TimeSpan.FromHours(8), Hours, NoHolidays).ShouldBe(Berlin(2031, 1, 20, 14));
        // Samstag gemeldet: Uhr startet Montag 07:00
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 18, 11), TimeSpan.FromHours(1), Hours, NoHolidays).ShouldBe(Berlin(2031, 1, 20, 8));
    }

    [Fact]
    public void AddBusinessTime_BeforeOpening_StartsAtOpening_AndSkipsHolidays()
    {
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 13, 5, 30), TimeSpan.FromMinutes(30), Hours, NoHolidays).ShouldBe(Berlin(2031, 1, 13, 7, 30));
        // Dienstag ist Betriebsruhe: Montag 16:00 + 2 h → Mittwoch 08:00
        SlaCalculator.AddBusinessTime(Berlin(2031, 1, 13, 16), TimeSpan.FromHours(2), Hours, new HashSet<DateOnly> { new(2031, 1, 14) })
            .ShouldBe(Berlin(2031, 1, 15, 8));
    }

    [Fact]
    public void AddBusinessTime_AcrossDaylightSavingChange_UsesLocalBusinessHours()
    {
        // Freitag, 28. März 2031, 16:00 (+01:00) + 2 h → Montag, 31. März 08:00 (+02:00)
        var due = SlaCalculator.AddBusinessTime(Berlin(2031, 3, 28, 16), TimeSpan.FromHours(2), Hours, NoHolidays);
        due.ShouldBe(Berlin(2031, 3, 31, 8));
        due.Offset.ShouldBe(TimeSpan.Zero, "Fälligkeiten werden in UTC gespeichert.");
    }

    [Fact]
    public void BusinessTimeBetween_CountsOnlyOpeningHours()
    {
        // Freitag 15:00 bis Montag 09:00 = 2 h + 2 h
        SlaCalculator.BusinessTimeBetween(Berlin(2031, 1, 17, 15), Berlin(2031, 1, 20, 9), Hours, NoHolidays).ShouldBe(TimeSpan.FromHours(4));
        SlaCalculator.BusinessTimeBetween(Berlin(2031, 1, 20, 9), Berlin(2031, 1, 17, 15), Hours, NoHolidays).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void DueDates_PerPriority_AndAroundTheClock()
    {
        var settings = new HelpdeskSettings(Targets: [new SlaTarget(TicketPriority.Urgent, 1, 4, AroundTheClock: true)]).Normalize();
        var created = Berlin(2031, 1, 17, 20); // Freitagabend
        var (response, resolution) = SlaCalculator.DueDates(created, TicketPriority.Urgent, 0, settings);
        response.ShouldBe(created.AddHours(1));
        resolution.ShouldBe(created.AddHours(4));

        // Normal: 8 h Reaktion, 40 h Lösung in Geschäftszeit (Standard).
        var (normalResponse, normalResolution) = SlaCalculator.DueDates(created, TicketPriority.Normal, 0, settings);
        normalResponse.ShouldBe(Berlin(2031, 1, 20, 15));
        normalResolution.ShouldBe(Berlin(2031, 1, 23, 17));

        // 60 Minuten Rückfrage verschieben das Lösungsziel um eine Stunde Geschäftszeit.
        SlaCalculator.DueDates(created, TicketPriority.Normal, 60, settings).Resolution.ShouldBe(Berlin(2031, 1, 24, 8));
    }

    [Fact]
    public void Settings_Normalize_FillsMissingTargets_AndRejectsInvertedHours()
    {
        var settings = new HelpdeskSettings(Targets: [new SlaTarget(TicketPriority.High, 2, 1)], Hours: new BusinessHours("18:00", "08:00"),
            Holidays: ["2031-12-24", "kein Datum"], AutoCloseDays: 500).Normalize();
        settings.Targets!.Count.ShouldBe(4);
        settings.Target(TicketPriority.High).ResolutionHours.ShouldBe(2, "Lösung nie vor der Reaktion.");
        settings.Hours!.Start.ShouldBe("07:00");
        settings.Holidays.ShouldBe(["2031-12-24"]);
        settings.AutoCloseDays.ShouldBe(60);
    }

    [Fact]
    public void State_ShowsOverdue_DueSoon_Paused_AndMet()
    {
        var now = Berlin(2031, 1, 13, 12);
        var ticket = Ticket.Create(Guid.CreateVersion7(), "T-2031-0001", "Drucker", null, TicketCategory.OfficeIt, TicketSource.Web, false, null, null,
            null, null, null, [], now.AddHours(-3));
        ticket.SetDueDates(now.AddHours(-1), now.AddHours(10));
        SlaCalculator.State(ticket, now).State.ShouldBe("overdue");
        SlaCalculator.State(ticket, now).DueKind.ShouldBe("response");

        ticket.MarkFirstResponse(now);
        SlaCalculator.State(ticket, now).ShouldBe(new SlaState("ok", now.AddHours(10), "resolution"));
        SlaCalculator.State(ticket, now.AddHours(9.5)).State.ShouldBe("dueSoon");

        ticket.ChangeStatus(TicketStatus.WaitingForReporter, null, now);
        SlaCalculator.State(ticket, now.AddDays(5)).State.ShouldBe("paused");

        ticket.ChangeStatus(TicketStatus.Resolved, "Toner gewechselt", now.AddHours(1));
        SlaCalculator.State(ticket, now.AddDays(5)).State.ShouldBe("met");
    }

    private static HelpdeskGroup Group(params Guid[] members) => HelpdeskGroup.Create(Guid.CreateVersion7(), "IT", null, members, true, 0);

    [Fact]
    public void AutoAssignment_FirstMatchingRuleInOrder_LeastBusyMember()
    {
        Guid anna = Guid.CreateVersion7(), ben = Guid.CreateVersion7(), clara = Guid.CreateVersion7();
        var it = Group(anna, ben, clara);
        var groups = new Dictionary<Guid, HelpdeskGroup> { [it.Id] = it };
        var rules = new[]
        {
            HelpdeskRule.Create(Guid.CreateVersion7(), "Alles andere", null, null, null, it.Id, AssignmentStrategy.GroupOnly, null, null, true, 99),
            HelpdeskRule.Create(Guid.CreateVersion7(), "Drucker", TicketCategory.OfficeIt, null, null, it.Id, AssignmentStrategy.LeastBusy, null,
                TicketPriority.High, true, 1),
            HelpdeskRule.Create(Guid.CreateVersion7(), "Inaktiv", TicketCategory.OfficeIt, null, null, null, AssignmentStrategy.Fixed, anna, null, false, 0),
        };
        var agents = new HashSet<Guid> { anna, ben }; // clara hat kein Bearbeitungsrecht
        var load = new Dictionary<Guid, int> { [anna] = 3, [ben] = 1 };

        var printer = AutoAssignment.Decide(rules, TicketCategory.OfficeIt, null, "Drucker druckt nicht", null, groups, agents, load)!;
        printer.RuleName.ShouldBe("Drucker");
        printer.AssigneeUserId.ShouldBe(ben);
        printer.GroupId.ShouldBe(it.Id);
        printer.Priority.ShouldBe(TicketPriority.High);

        var other = AutoAssignment.Decide(rules, TicketCategory.Sync, null, "Fotos fehlen", null, groups, agents, load)!;
        other.RuleName.ShouldBe("Alles andere");
        other.AssigneeUserId.ShouldBeNull();

        AutoAssignment.LeastBusy(it, agents, new Dictionary<Guid, int>()).ShouldBe(anna, "Gleichstand: erste in der Gruppe.");
        AutoAssignment.LeastBusy(Group(clara), agents, load).ShouldBeNull();
    }

    [Fact]
    public void AutoAssignment_KeywordAndSubcategory_FixedOnlyForAgents()
    {
        Guid anna = Guid.CreateVersion7(), gone = Guid.CreateVersion7();
        var sub = Guid.CreateVersion7();
        var rules = new[]
        {
            HelpdeskRule.Create(Guid.CreateVersion7(), "VPN", null, null, "vpn", null, AssignmentStrategy.Fixed, anna, null, true, 1),
            HelpdeskRule.Create(Guid.CreateVersion7(), "Telefon", TicketCategory.OfficeIt, sub, null, null, AssignmentStrategy.Fixed, gone, null, true, 2),
        };
        var agents = new HashSet<Guid> { anna };
        var empty = new Dictionary<Guid, HelpdeskGroup>();
        var none = new Dictionary<Guid, int>();

        AutoAssignment.Decide(rules, TicketCategory.Access, null, "Kein Zugriff", "VPN bricht ab", empty, agents, none)!.AssigneeUserId.ShouldBe(anna);
        var phone = AutoAssignment.Decide(rules, TicketCategory.OfficeIt, sub, "Telefon klingelt nicht", null, empty, agents, none)!;
        phone.RuleName.ShouldBe("Telefon");
        phone.AssigneeUserId.ShouldBeNull("Die feste Person hat kein Bearbeitungsrecht mehr.");
        AutoAssignment.Decide(rules, TicketCategory.OfficeIt, null, "Telefon klingelt nicht", null, empty, agents, none).ShouldBeNull();
        Should.Throw<ArgumentException>(() =>
            HelpdeskRule.Create(Guid.CreateVersion7(), "Ohne Gruppe", null, null, null, null, AssignmentStrategy.LeastBusy, null, null, true, 0));
    }

    [Fact]
    public void KnowledgeSearch_FindsByTitleKeywordsAndUmlauts_PublishedOnly()
    {
        var user = Guid.CreateVersion7();
        var printer = KnowledgeArticle.Create(Guid.CreateVersion7(), "Drucker im Büro: Papierstau beheben", "Klappe öffnen, Papier entfernen.",
            TicketCategory.OfficeIt, "drucker, papier", true, user);
        var sync = KnowledgeArticle.Create(Guid.CreateVersion7(), "Fotos werden nicht übertragen", "WLAN prüfen, App öffnen, „Jetzt abgleichen“.",
            TicketCategory.Sync, "upload foto synchronisation", true, user);
        var draft = KnowledgeArticle.Create(Guid.CreateVersion7(), "Drucker neu einrichten (Entwurf)", "…", TicketCategory.OfficeIt, null, false, user);

        var found = KnowledgeSearch.Rank([printer, sync, draft], "Der Drucker im Buero meldet Papierstau", TicketCategory.OfficeIt, publishedOnly: true);
        found.Select(a => a.Id).ShouldBe([printer.Id]);
        KnowledgeSearch.Rank([printer, sync, draft], "Foto Übertragung hängt", null, publishedOnly: true)[0].Id.ShouldBe(sync.Id);
        KnowledgeSearch.Rank([printer, sync, draft], "und oder der", null, publishedOnly: false).ShouldBeEmpty();
        KnowledgeSearch.Rank([printer, sync, draft], "Drucker einrichten", null, publishedOnly: false).Select(a => a.Id).ShouldContain(draft.Id);
    }
}
