using KCrashLab.Controller;
using KCrashLab.Contracts;
using KCrashLab.Domain;
using System.Security.Cryptography;
using System.Text;

namespace KCrashLab.Tests;

public sealed class RealLabEvidenceTests
{
    [Fact]
    public void RealParserFailsClosedWithoutBugcheckMarker()
    {
        Assert.Throws<InvalidDataException>(() => RealWindbgParser.Parse("MODULE_NAME: KCrashLabTarget\nSTACK_TEXT:\n"));
        Assert.Throws<InvalidDataException>(() => RealWindbgParser.Parse(new string('x', RealWindbgParser.MaximumInputChars + 1)));
    }

    [Fact]
    public async Task ParserAndPublicBundleRequireThreeMatchingReplaysAndWithholdDump()
    {
        var raw = await File.ReadAllTextAsync(TestPaths.Fixture("triage", "real-windbg.txt"));
        var analysis = RealWindbgParser.Parse(raw);
        var signature = SignatureV1.Compute(analysis);
        Assert.Equal("REAL_LAB", analysis.ExecutionMode);
        Assert.Equal("e2c1a501", analysis.BugcheckCode);
        Assert.Equal("kcrashlabtarget", analysis.FaultingModule);
        Assert.Equal(ParseConfidence.High, analysis.ParseConfidence);

        var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "kmdf-state-crash.case.json")));
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var replays = Enumerable.Range(1, 3)
                .Select(attempt => new RealLabReplayRecord(attempt, "MATCH", signature, Hash((char)('a' + attempt)), Hash('7'), Hash('8')))
                .ToArray();
            var input = new RealLabEvidenceInput(original, original, Hash('d'), Hash('e'), Hash('f'), Hash('6'),
                "Windows 11 build 26100", Hash('9'), Hash('a'), RawHash(raw), Hash('7'), Hash('8'), raw, replays, DateTimeOffset.Parse("2026-08-31T00:00:00Z"), new string('1', 40));
            await RealLabEvidence.BuildAsync(temporary, input, CancellationToken.None);
            var verified = await RealLabEvidence.VerifyAsync(temporary, CancellationToken.None);

            Assert.True(verified.IsValid, string.Join(Environment.NewLine, verified.Errors));
            Assert.Empty(Directory.EnumerateFiles(temporary, "*.dmp", SearchOption.AllDirectories));
            Assert.Contains("WITHHELD_SENSITIVE_KERNEL_MEMORY", await File.ReadAllTextAsync(Path.Combine(temporary, "private-artifacts.json")));
            Assert.False(File.Exists(Path.Combine(temporary, "crash", "windbg.raw.txt")));
            Assert.True(File.Exists(Path.Combine(temporary, "crash", "windbg.sanitized.txt")));

            await File.WriteAllBytesAsync(Path.Combine(temporary, "leaked.DMP"), [1, 2, 3]);
            var leaked = await RealLabEvidence.VerifyAsync(temporary, CancellationToken.None);
            Assert.False(leaked.IsValid);
            Assert.Contains(leaked.Errors, static error => error.Contains("must not contain a memory dump", StringComparison.Ordinal));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task BuilderRejectsDivergentReplay()
    {
        var raw = await File.ReadAllTextAsync(TestPaths.Fixture("triage", "real-windbg.txt"));
        var analysis = RealWindbgParser.Parse(raw);
        var signature = SignatureV1.Compute(analysis);
        var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "kmdf-state-crash.case.json")));
        var input = new RealLabEvidenceInput(original, original, Hash('d'), Hash('e'), Hash('f'), Hash('6'), "Windows", Hash('9'),
            Hash('a'), RawHash(raw), Hash('7'), Hash('8'), raw, [new(1, "MATCH", signature, Hash('a'), Hash('7'), Hash('8')), new(2, "DIVERGENT", Hash('b'), Hash('b'), Hash('7'), Hash('8')), new(3, "MATCH", signature, Hash('c'), Hash('7'), Hash('8'))],
            DateTimeOffset.UtcNow, new string('1', 40));
        var temporary = TestPaths.NewTemporaryDirectory();
        try { await Assert.ThrowsAsync<InvalidDataException>(() => RealLabEvidence.BuildAsync(temporary, input, CancellationToken.None)); }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static string Hash(char value) => new(value, 64);
    private static string RawHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
