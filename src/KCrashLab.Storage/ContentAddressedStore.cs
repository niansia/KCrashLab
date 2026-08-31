using System.Security.Cryptography;

namespace KCrashLab.Storage;

public sealed record StoredBlob(string Sha256, long Length, string Path, bool AlreadyExisted);

public sealed class ContentAddressedStore
{
    private readonly string root;

    public ContentAddressedStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public async Task<StoredBlob> PutAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var staging = Path.Combine(root, $".staging-{Guid.NewGuid():N}.tmp");
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            await using (var destination = new FileStream(
                staging,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                131_072,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[131_072];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    length += read;
                    hasher.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }

            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var directory = Path.Combine(root, hash[..2]);
            Directory.CreateDirectory(directory);
            var finalPath = Path.Combine(directory, hash);
            if (File.Exists(finalPath))
            {
                await using var existing = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var existingHash = Convert.ToHexString(await SHA256.HashDataAsync(existing, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
                if (existing.Length != length || !string.Equals(existingHash, hash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Existing CAS blob does not match its content address.");
                }

                File.Delete(staging);
                return new StoredBlob(hash, length, finalPath, true);
            }

            File.Move(staging, finalPath);
            return new StoredBlob(hash, length, finalPath, false);
        }
        catch
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }

            throw;
        }
    }
}
