using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Verschlüsselung ausgelagerter Originale (AES-256-GCM in Blöcken zu 1 MiB, Format „EOO1“). Der Schlüssel je Objekt wird
/// per HKDF-SHA-256 aus dem Hauptschlüssel der Auslagerung und der Objekt-ID abgeleitet; der Hauptschlüssel liegt nur auf
/// dem EstrichONE-Server (Datenbank, mit ASP.NET Data Protection verschlüsselt – Schlüsselring <c>.keys</c> in der Sicherung).
/// Jeder Block ist authentifiziert (Blocknummer und „letzter Block“ in den Zusatzdaten): Verändern, Vertauschen oder
/// Abschneiden fällt beim Entschlüsseln auf. Gleiche Eingabe und gleicher Nonce-Präfix ergeben dieselbe Ausgabe
/// (Prüfsumme vorab berechnen, dann streamen).
/// </summary>
internal static class OffloadCrypto
{
    public const int ChunkSize = 1024 * 1024;
    public const int HeaderSize = 16;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "EOO1"u8.ToArray();

    public static byte[] DeriveKey(byte[] masterKey, Guid objectId) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, objectId.ToByteArray(), "estrichone-offload-v1"u8.ToArray());

    public static long CipherLength(long plainLength)
    {
        var chunks = plainLength == 0 ? 1 : (plainLength + ChunkSize - 1) / ChunkSize;
        return HeaderSize + plainLength + (chunks * TagSize);
    }

    public static async Task EncryptAsync(Stream input, Stream output, byte[] key, byte[] noncePrefix, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(noncePrefix.Length, 8);
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), ChunkSize);
        noncePrefix.CopyTo(header, 8);
        await output.WriteAsync(header, cancellationToken);

        using var aes = new AesGcm(key, TagSize);
        var current = new byte[ChunkSize];
        var next = new byte[ChunkSize];
        var cipher = new byte[ChunkSize + TagSize];
        var currentLength = await FillAsync(input, current, cancellationToken);
        uint counter = 0;
        while (true)
        {
            var nextLength = currentLength == ChunkSize ? await FillAsync(input, next, cancellationToken) : 0;
            var final = nextLength == 0;
            Seal(aes, header, counter, final, current.AsSpan(0, currentLength), cipher);
            await output.WriteAsync(cipher.AsMemory(0, currentLength + TagSize), cancellationToken);
            if (final)
            {
                break;
            }

            (current, next) = (next, current);
            currentLength = nextLength;
            counter = checked(counter + 1);
        }

        await output.FlushAsync(cancellationToken);
    }

    public static async Task DecryptAsync(Stream input, Stream output, byte[] key, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        if (await FillAsync(input, header, cancellationToken) != HeaderSize || !header.AsSpan(0, 4).SequenceEqual(Magic)
            || BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)) != ChunkSize)
        {
            throw new CryptographicException("Kein gültiges ausgelagertes Objekt (Kopf fehlt oder unbekanntes Format).");
        }

        using var aes = new AesGcm(key, TagSize);
        var current = new byte[ChunkSize + TagSize];
        var next = new byte[ChunkSize + TagSize];
        var plain = new byte[ChunkSize];
        var currentLength = await FillAsync(input, current, cancellationToken);
        uint counter = 0;
        while (true)
        {
            if (currentLength < TagSize)
            {
                throw new CryptographicException("Ausgelagertes Objekt ist abgeschnitten.");
            }

            var nextLength = currentLength == current.Length ? await FillAsync(input, next, cancellationToken) : 0;
            var final = nextLength == 0;
            var plainLength = currentLength - TagSize;
            Open(aes, header, counter, final, current.AsSpan(0, currentLength), plain.AsSpan(0, plainLength));
            await output.WriteAsync(plain.AsMemory(0, plainLength), cancellationToken);
            if (final)
            {
                break;
            }

            (current, next) = (next, current);
            currentLength = nextLength;
            counter = checked(counter + 1);
        }

        await output.FlushAsync(cancellationToken);
    }

    private static void Seal(AesGcm aes, byte[] header, uint counter, bool final, ReadOnlySpan<byte> plain, Span<byte> output)
    {
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[HeaderSize + 5];
        Prepare(header, counter, final, nonce, aad);
        aes.Encrypt(nonce, plain, output[..plain.Length], output.Slice(plain.Length, TagSize), aad);
    }

    private static void Open(AesGcm aes, byte[] header, uint counter, bool final, ReadOnlySpan<byte> cipher, Span<byte> plain)
    {
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[HeaderSize + 5];
        Prepare(header, counter, final, nonce, aad);
        aes.Decrypt(nonce, cipher[..^TagSize], cipher[^TagSize..], plain, aad);
    }

    private static void Prepare(byte[] header, uint counter, bool final, Span<byte> nonce, Span<byte> aad)
    {
        header.AsSpan(8, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], counter);
        header.CopyTo(aad);
        BinaryPrimitives.WriteUInt32BigEndian(aad[HeaderSize..], counter);
        aad[HeaderSize + 4] = final ? (byte)1 : (byte)0;
    }

    private static async Task<int> FillAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

/// <summary>Schreibziel, das nur die SHA-256 und die Länge des Geschriebenen ermittelt (Prüfsumme vor dem Upload).</summary>
internal sealed class HashingSink : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Stream? _inner;

    public HashingSink(Stream? inner = null) => _inner = inner;

    public long Written { get; private set; }

    public string Sha256() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Written;
    public override long Position { get => Written; set => throw new NotSupportedException(); }

    public override void Flush() => _inner?.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _hash.AppendData(buffer, offset, count);
        Written += count;
        _inner?.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _hash.AppendData(buffer.Span);
        Written += buffer.Length;
        if (_inner is not null)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Lesequelle, die nebenbei die SHA-256 des Gelesenen bildet (Original vor dem Verschlüsseln prüfen).</summary>
internal sealed class HashingSource(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long ReadBytes { get; private set; }

    public string Sha256() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _hash.AppendData(buffer, offset, read);
        ReadBytes += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span[..read]);
        ReadBytes += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
