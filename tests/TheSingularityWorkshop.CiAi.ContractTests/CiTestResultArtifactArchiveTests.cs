using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class CiTestResultArtifactArchiveTests
{
    public static int Run()
    {
        var trx = Encoding.UTF8.GetBytes("<TestRun />");
        var provenance = Encoding.UTF8.GetBytes("{\"schema\":\"workshop.test-artifact-provenance.v1\"}");
        var archiveBytes = CreateArchive(("minesweeper.trx", trx), ("provenance.json", provenance));
        var providerDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();

        var verified = CiTestResultArtifactArchive.Read(
            archiveBytes, providerDigest, "minesweeper.trx", "provenance.json");
        Assert(verified.ArchiveSizeBytes == archiveBytes.Length &&
            verified.TrxBytes.Span.SequenceEqual(trx) &&
            verified.ProvenanceBytes.Span.SequenceEqual(provenance) &&
            verified.TrxSha256 == Convert.ToHexString(SHA256.HashData(trx)).ToLowerInvariant(),
            "verified archive did not preserve exact payload bytes and digests");

        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            archiveBytes, "sha256:" + new string('0', 64), "minesweeper.trx", "provenance.json"));
        Throws<ArgumentException>(() => CiTestResultArtifactArchive.Read(
            archiveBytes, providerDigest, "../minesweeper.trx", "provenance.json"));
        Throws<ArgumentException>(() => CiTestResultArtifactArchive.Read(
            archiveBytes, providerDigest, "minesweeper.trx", "folder/provenance.json"));
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            archiveBytes, providerDigest, "other.trx", "provenance.json"));
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            archiveBytes, providerDigest, "minesweeper.trx", "provenance.json",
            maximumArchiveBytes: archiveBytes.Length - 1));

        var duplicate = CreateArchive(
            ("minesweeper.trx", trx),
            ("minesweeper.trx", trx));
        var duplicateDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(duplicate)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            duplicate, duplicateDigest, "minesweeper.trx", "provenance.json"));

        var extra = CreateArchive(
            ("minesweeper.trx", trx),
            ("provenance.json", provenance),
            ("unexpected.txt", Encoding.UTF8.GetBytes("extra")));
        var extraDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(extra)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            extra, extraDigest, "minesweeper.trx", "provenance.json"));

        var traversal = CreateArchive(
            ("../minesweeper.trx", trx),
            ("provenance.json", provenance));
        var traversalDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(traversal)).ToLowerInvariant();
        Throws<ArgumentException>(() => CiTestResultArtifactArchive.Read(
            traversal, traversalDigest, "minesweeper.trx", "provenance.json"));

        var symlink = CreateArchive(
            ("minesweeper.trx", trx, 0xA000 << 16),
            ("provenance.json", provenance));
        var symlinkDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(symlink)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            symlink, symlinkDigest, "minesweeper.trx", "provenance.json"));

        var oversized = CreateArchive(
            ("minesweeper.trx", new byte[128]),
            ("provenance.json", provenance));
        var oversizedDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(oversized)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            oversized, oversizedDigest, "minesweeper.trx", "provenance.json",
            maximumPayloadBytes: 64));

        var compressionBomb = CreateArchive(
            ("minesweeper.trx", Enumerable.Repeat((byte)'A', 20_000).ToArray()),
            ("provenance.json", provenance));
        var bombDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(compressionBomb)).ToLowerInvariant();
        Throws<InvalidDataException>(() => CiTestResultArtifactArchive.Read(
            compressionBomb, bombDigest, "minesweeper.trx", "provenance.json"));

        Console.WriteLine("PASS: 10 verified test-artifact ZIP boundary checks.");
        return 10;
    }

    private static byte[] CreateArchive(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
                if (item.Name == "minesweeper.trx" && item.Bytes.Length == 0)
                    entry.ExternalAttributes = 0;
                using var target = entry.Open();
                target.Write(item.Bytes);
            }
        }
        return output.ToArray();
    }

    private static byte[] CreateArchive(params (string Name, byte[] Bytes, int Attributes)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
                entry.ExternalAttributes = item.Attributes;
                using var target = entry.Open();
                target.Write(item.Bytes);
            }
        }
        return output.ToArray();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
