namespace EstrichOne.Api.Infrastructure.Persistence;

/// <summary>
/// Datenumstellung „Hinweise und Zugang“ (Migration SiteNotesAndAccess): Das frühere Feld „Zugang / Besonderheiten“
/// (<c>project.siteAccess</c>) geht in die Hinweise der Bauakte (<c>site_notes</c>) auf.
/// Das SQL ist wiederholbar: Ein zweiter Lauf findet nichts mehr und ändert nichts.
/// </summary>
public static class SiteNotesAccessMerge
{
    /// <summary>
    /// Je Bauakte mit <c>project.siteAccess</c>: Text mit Leerzeile an die Hinweise anhängen (nicht, wenn er dort schon
    /// wortgleich steht), danach den Schlüssel aus <c>project</c> entfernen. Hinweise (bis 2000) und Zugang (bis 1000) passen
    /// immer in die neue Länge von 3000 Zeichen; reicht es für die Leerzeile nicht, wird enger getrennt, verloren geht nichts.
    /// Version und Änderungsfeed (Trigger) ziehen mit, damit Apps den neuen Stand abholen.
    /// </summary>
    public const string Bauakten = """
        UPDATE bauakten b
        SET site_notes = CASE
                WHEN m.access = '' OR position(m.access in m.notes) > 0 THEN NULLIF(m.notes, '')
                WHEN m.notes = '' THEN m.access
                WHEN char_length(m.notes) + char_length(m.access) + 2 <= 3000 THEN m.notes || E'\n\n' || m.access
                WHEN char_length(m.notes) + char_length(m.access) + 1 <= 3000 THEN m.notes || E'\n' || m.access
                ELSE m.notes || m.access
            END,
            project = b.project - 'siteAccess',
            version = b.version + 1,
            updated_at = now()
        FROM (
            SELECT id,
                   btrim(coalesce(site_notes, ''), E' \t\r\n') AS notes,
                   btrim(coalesce(project ->> 'siteAccess', ''), E' \t\r\n') AS access
            FROM bauakten
            WHERE jsonb_typeof(project) = 'object' AND jsonb_exists(project, 'siteAccess')
        ) m
        WHERE b.id = m.id;
        """;

    /// <summary>Eingebautes Feld „siteAccess“ aus dem gespeicherten Formular „bauakte“ entfernen (Gruppen und Reihenfolge bleiben).</summary>
    public const string FormSchemas = """
        UPDATE form_schemas s
        SET definition = (
                SELECT coalesce(jsonb_agg(g.value || jsonb_build_object('fields', coalesce((
                           SELECT jsonb_agg(f.value ORDER BY f.ordinality)
                           FROM jsonb_array_elements(g.value -> 'fields') WITH ORDINALITY f
                           WHERE f.value ->> 'key' IS DISTINCT FROM 'siteAccess'), '[]'::jsonb)) ORDER BY g.ordinality), '[]'::jsonb)
                FROM jsonb_array_elements(s.definition) WITH ORDINALITY g),
            version = s.version + 1,
            updated_at = now()
        WHERE s.key = 'bauakte' AND jsonb_typeof(s.definition) = 'array'
          AND EXISTS (
                SELECT 1 FROM jsonb_array_elements(s.definition) g, jsonb_array_elements(g.value -> 'fields') f
                WHERE jsonb_typeof(g.value -> 'fields') = 'array' AND f.value ->> 'key' = 'siteAccess');
        """;
}
