namespace EstrichOne.Hosting.Proxy;

/// <summary>Statische Wartungsseite (503) für gesperrte, angehaltene oder nicht erreichbare Instanzen.</summary>
public static class MaintenancePage
{
    public static string Build(string? logoDataUri) => $$"""
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>EstrichONE – vorübergehend nicht erreichbar</title>
        <style>
          :root { color-scheme: light dark; --bg: #f5f6f7; --card: #ffffff; --text: #1d2d3e; --muted: #556b82; --accent: #0b2d5b; }
          @media (prefers-color-scheme: dark) { :root { --bg: #12171c; --card: #1d232a; --text: #eaecee; --muted: #a9b4be; --accent: #8ec5ff; } }
          * { box-sizing: border-box; }
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: var(--bg); color: var(--text);
                 font-family: "72", "Segoe UI", system-ui, -apple-system, Roboto, sans-serif; padding: 24px; }
          main { max-width: 520px; width: 100%; background: var(--card); border-radius: 16px; padding: 40px 36px;
                 box-shadow: 0 10px 40px rgba(0,0,0,.08); text-align: center; }
          img { width: 72px; height: 72px; }
          h1 { font-size: 1.5rem; margin: 20px 0 8px; color: var(--accent); }
          p { line-height: 1.55; color: var(--muted); margin: 8px 0; }
          small { display: block; margin-top: 24px; color: var(--muted); }
        </style>
        </head>
        <body>
        <main>
          {{(logoDataUri is null ? string.Empty : $"<img src=\"{logoDataUri}\" alt=\"EstrichONE\">")}}
          <h1>Vorübergehend nicht erreichbar</h1>
          <p>Diese EstrichONE-Instanz ist im Moment nicht verfügbar – wegen Wartungsarbeiten oder auf Veranlassung des Betreibers.</p>
          <p>Ihre Daten sind sicher gespeichert. Bitte versuchen Sie es später erneut oder wenden Sie sich an Ihren Ansprechpartner.</p>
          <small>EstrichONE Hosting</small>
        </main>
        </body>
        </html>
        """;
}
