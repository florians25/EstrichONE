using System.Text;

namespace EstrichOne.Api.Infrastructure.Text;

/// <summary>
/// CSV nach RFC 4180 (Anführungszeichen, Zeilenumbrüche in Feldern). Erkennt Trennzeichen (<c>;</c> wie in
/// deutschem Excel, oder <c>,</c>) und Kodierung (UTF-8, sonst Windows-1252 – Standard bei Excel-Exporten).
/// </summary>
public static class CsvDocument
{
    static CsvDocument() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            return text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(content);
        }
    }

    /// <summary>Zeilen mit ihrer (1-basierten) Zeilennummer in der Datei; leere Zeilen werden übersprungen.</summary>
    public static IReadOnlyList<(int Line, IReadOnlyList<string> Fields)> Parse(string text)
    {
        var separator = DetectSeparator(text);
        var rows = new List<(int, IReadOnlyList<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowStart = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
                line++;
                rowStart = line;
            }
            else
            {
                field.Append(c);
            }
        }

        EndRow();
        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Any(value => value.Trim().Length > 0))
            {
                rows.Add((rowStart, fields.Select(value => value.Trim()).ToList()));
            }

            fields = [];
        }
    }

    private static char DetectSeparator(string text)
    {
        var header = text.Split('\n', 2)[0];
        return header.Count(c => c == ';') >= header.Count(c => c == ',') ? ';' : ',';
    }
}
