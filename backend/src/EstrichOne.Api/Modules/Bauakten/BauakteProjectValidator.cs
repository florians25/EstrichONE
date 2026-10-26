using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization.Exports;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Kennwerte prüfen: Form der Werte und Auswahlwerte gegen die Wertelisten der Stammdaten (B6).</summary>
internal sealed class BauakteProjectValidator(IValueLists valueLists)
{
    public async Task<string?> ValidateAsync(BauakteProject project, CancellationToken cancellationToken)
    {
        var p = project.Normalize();
        if (p.ValidateShape() is { } error)
        {
            return error;
        }

        foreach (var (list, read) in BauakteProject.ListFields)
        {
            if (read(p) is { } value && !await valueLists.ContainsAsync(list, value, cancellationToken))
            {
                return $"Unbekannter Wert „{value}“ für {list} – bitte in den Stammdaten anlegen.";
            }
        }

        return null;
    }
}
