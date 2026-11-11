namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Ergebnis der automatischen Zuweisung.</summary>
public sealed record AssignmentDecision(Guid? RuleId, string? RuleName, Guid? GroupId, Guid? AssigneeUserId, TicketPriority? Priority);

/// <summary>
/// Automatische Zuweisung neuer Tickets (rein, ohne Datenbank – siehe Tests): erste aktive Regel in Reihenfolge, deren
/// Bedingungen passen (Kategorie, Unterkategorie, Stichwort). „Wenigste offene Tickets“ wählt unter den aktiven
/// Gruppenmitgliedern mit Bearbeitungsrecht die Person mit der geringsten Last, bei Gleichstand die erste in der Gruppe.
/// </summary>
public static class AutoAssignment
{
    public static AssignmentDecision? Decide(
        IEnumerable<HelpdeskRule> rules, TicketCategory category, Guid? subcategoryId, string title, string? description,
        IReadOnlyDictionary<Guid, HelpdeskGroup> groups, IReadOnlySet<Guid> eligibleAgents, IReadOnlyDictionary<Guid, int> openTickets)
    {
        var text = $"{title}\n{description}";
        foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            if ((rule.Category is { } c && c != category)
                || (rule.SubcategoryId is { } s && s != subcategoryId)
                || (rule.Keyword is { } k && !text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var group = rule.GroupId is { } g && groups.TryGetValue(g, out var found) && found.IsActive ? found : null;
            Guid? assignee = rule.Strategy switch
            {
                AssignmentStrategy.Fixed when rule.AssigneeUserId is { } user && eligibleAgents.Contains(user) => user,
                AssignmentStrategy.LeastBusy when group is not null => LeastBusy(group, eligibleAgents, openTickets),
                _ => null,
            };
            return new AssignmentDecision(rule.Id, rule.Name, group?.Id, assignee, rule.Priority);
        }

        return null;
    }

    public static Guid? LeastBusy(HelpdeskGroup group, IReadOnlySet<Guid> eligibleAgents, IReadOnlyDictionary<Guid, int> openTickets)
    {
        var candidates = group.MemberUserIds.Where(eligibleAgents.Contains).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // Stabile Reihenfolge: bei gleicher Last die erste in der Gruppe.
        return candidates.Select((id, index) => (id, index, load: openTickets.GetValueOrDefault(id)))
            .OrderBy(x => x.load).ThenBy(x => x.index).First().id;
    }
}
