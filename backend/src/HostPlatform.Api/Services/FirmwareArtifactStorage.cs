using System.Security.Cryptography;

namespace HostPlatform.Api.Services;

/// <summary>Verified, immutable artifact bytes under the configured payload root.</summary>
internal static class FirmwareArtifactStorage
{
    public const int MaximumBytes = 50_000_000;

    private static string Root(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Payload storage is not configured.");
        var root = Path.GetFullPath(configured.Trim());
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Payload storage must be an existing, nonredirected directory.");
        return root;
    }

    private static string ExistingPath(string? configured, string storageRef)
    {
        var root = Root(configured);
        storageRef = storageRef.Trim();
        if (storageRef.Length == 0 || storageRef.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageRef))
            throw new InvalidOperationException("Artifact reference must be a relative path under payload storage.");
        var full = Path.GetFullPath(Path.Combine(root, storageRef));
        var relative = Path.GetRelativePath(root, full);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Artifact reference must stay within payload storage.");
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Artifact reference must not use a redirected storage path.");
        }
        return full;
    }

    public static bool PayloadPresent(string? root, string storageRef, long expectedSize)
    {
        try { return expectedSize is > 0 and <= MaximumBytes && new FileInfo(ExistingPath(root, storageRef)).Length == expectedSize; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException) { return false; }
    }

    public static async Task<byte[]> ReadVerifiedAsync(string? root, string storageRef, long expectedSize, string expectedDigest, CancellationToken ct)
    {
        try
        {
            if (expectedSize is <= 0 or > MaximumBytes) throw new InvalidOperationException("Artifact must have a registered size between 1 byte and 50 MB.");
            if (expectedDigest.Length != 64 || !expectedDigest.All(Uri.IsHexDigit)) throw new InvalidOperationException("Artifact must have a registered SHA-256 digest.");
            await using var stream = new FileStream(ExistingPath(root, storageRef), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (stream.Length != expectedSize) throw new InvalidOperationException("Artifact blob length does not match the registered size.");
            var bytes = new byte[(int)expectedSize];
            await stream.ReadExactlyAsync(bytes, ct);
            if (await stream.ReadAsync(new byte[1], ct) != 0) throw new InvalidOperationException("Artifact blob length changed while reading.");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedDigest)))
                throw new InvalidOperationException("Artifact blob SHA-256 does not match the registry.");
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InvalidOperationException("The registered artifact blob could not be read from payload storage.", ex); }
    }

    public static async Task<string> StorePrimaryAsync(string? configured, Stream input, long size, byte[] checksum, CancellationToken ct)
    {
        if (size is <= 0 or > MaximumBytes || checksum.Length != 32)
            throw new InvalidOperationException("The package must have a registered size between 1 byte and 50 MB and a SHA-256 digest.");
        var digest = Convert.ToHexString(checksum).ToLowerInvariant();
        var relative = $"firmware/{digest}.bin";
        string? temporary = null;
        try
        {
            var root = Root(configured);
            var folder = Path.Combine(root, "firmware");
            Directory.CreateDirectory(folder);
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Artifact storage must not use a redirected directory.");
            var destination = Path.Combine(folder, digest + ".bin");
            temporary = Path.Combine(folder, $".upload-{Guid.NewGuid():N}.tmp");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long count = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                while (count < size)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, size - count)), ct);
                    if (read == 0) break;
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct); count += read;
                }
                if (count != size || await input.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
                    throw new InvalidOperationException("Uploaded artifact length does not match the registered package size.");
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), checksum))
                    throw new InvalidOperationException("Uploaded artifact SHA-256 does not match the registered package.");
                await output.FlushAsync(ct);
            }
            // Only install verified bytes. A concurrent identical upload reuses
            // the existing immutable blob after independently verifying it.
            try { File.Move(temporary, destination, overwrite: false); temporary = null; }
            catch (IOException) when (File.Exists(destination))
            { _ = await ReadVerifiedAsync(configured, relative, size, digest, ct); }
            return relative;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InvalidOperationException("The artifact could not be stored in configured payload storage.", ex); }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
    }
}
