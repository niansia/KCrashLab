using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Domain;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record RealLabReplayRecord(
    int Attempt,
    string Classification,
    string? Signature,
    string DumpSha256,
    string JournalSha256,
    string WatchdogSha256);

public sealed record RealLabEvidenceInput(
    CanonicalCase Original,
    CanonicalCase Minimized,
    string DriverSha256,
    string VmIdentitySha256,
    string CheckpointIdentitySha256,
    string AuthorizationSha256,
    string GuestBuild,
    string SymbolsSha256,
    string DumpSha256,
    string RawWindbgSha256,
    string DiscoveryJournalSha256,
    string DiscoveryWatchdogSha256,
    string RawWindbg,
    IReadOnlyList<RealLabReplayRecord> Replays,
    DateTimeOffset RecordedAtUtc,
    string GitCommit);

public sealed record RealLabEvidenceVerification(bool IsValid, int VerifiedFiles, IReadOnlyList<string> Errors);

public static class RealLabEvidence
{
    public static async Task BuildAsync(string root, RealLabEvidenceInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Original is null || input.Minimized is null || input.Replays is null || input.RawWindbg is null)
            throw new InvalidDataException("Real-lab evidence input is missing required objects.");
        ValidateHash(input.DriverSha256, "driver");
        ValidateHash(input.VmIdentitySha256, "VM identity");
        ValidateHash(input.CheckpointIdentitySha256, "checkpoint identity");
        ValidateHash(input.AuthorizationSha256, "authorization identity");
        ValidateHash(input.SymbolsSha256, "symbols identity");
        ValidateHash(input.DumpSha256, "dump");
        ValidateHash(input.RawWindbgSha256, "raw WinDbg");
        ValidateHash(input.DiscoveryJournalSha256, "discovery journal");
        ValidateHash(input.DiscoveryWatchdogSha256, "discovery watchdog");
        var computedRawHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.RawWindbg))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(computedRawHash), Convert.FromHexString(input.RawWindbgSha256)))
            throw new InvalidDataException("Raw WinDbg content does not match its acquisition SHA-256.");
        if (input.GitCommit is not { Length: 40 } || input.GitCommit.Any(static item => item is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Git commit must be a lowercase full 40-character object ID.");
        if (input.Replays.Count != 3) throw new InvalidDataException("A public real-lab bundle requires exactly three cold replay records.");
        if (!input.Replays.Select(static replay => replay.Attempt).Order().SequenceEqual(new[] { 1, 2, 3 }))
            throw new InvalidDataException("Cold replay attempts must be numbered 1, 2, and 3 exactly once.");
        foreach (var replay in input.Replays)
        {
            ValidateHash(replay.DumpSha256, $"replay {replay.Attempt} dump");
            ValidateHash(replay.JournalSha256, $"replay {replay.Attempt} journal");
            ValidateHash(replay.WatchdogSha256, $"replay {replay.Attempt} watchdog");
        }
        if (input.Original.Value.Target != "kcl.kmdf" || input.Minimized.Value.Target != input.Original.Value.Target)
            throw new InvalidDataException("Real-lab evidence accepts only the repository-owned kcl.kmdf target.");
        if (input.Minimized.Value.Operations.Count > input.Original.Value.Operations.Count)
            throw new InvalidDataException("Minimized case cannot contain more operations than the original case.");
        var analysis = RealWindbgParser.Parse(input.RawWindbg);
        var signature = SignatureV1.Compute(analysis);
        if (input.Replays.Any(replay => replay.Classification != "MATCH" || replay.Signature != signature))
            throw new InvalidDataException("All three cold replays must match the discovery signature.");

        var fullRoot = Path.GetFullPath(root);
        if (Directory.Exists(fullRoot) && Directory.EnumerateFileSystemEntries(fullRoot).Any())
            throw new InvalidDataException("Real-lab evidence output directory must be empty.");
        Directory.CreateDirectory(fullRoot);
        await WriteJson(Path.Combine(fullRoot, "finding.json"), new
        {
            schema_version = 1, execution_mode = "REAL_LAB", classification = "CONFIRMED_SYNTHETIC_DRIVER_CRASH",
            signature_version = 1, signature, case_id = input.Original.CaseId, minimized_case_id = input.Minimized.CaseId,
            claims = new { kernel_crash = "CONFIRMED_IN_ISOLATED_LAB", root_cause = "SYNTHETIC_BUGCHECK_ORACLE", exploitability = "NOT_ASSESSED" }
        }, cancellationToken).ConfigureAwait(false);
        await WriteJson(Path.Combine(fullRoot, "environment.json"), new
        {
            schema_version = 1, execution_mode = "REAL_LAB", input.GuestBuild, input.DriverSha256,
            input.VmIdentitySha256, input.CheckpointIdentitySha256, input.AuthorizationSha256, input.SymbolsSha256, recorded_at_utc = input.RecordedAtUtc, input.GitCommit
        }, cancellationToken).ConfigureAwait(false);
        await WriteJson(Path.Combine(fullRoot, "decision.json"), new
        {
            schema_version = 1, execution_mode = "REAL_LAB", status = "CONFIRMED", replay_policy = "3/3_COLD_CHECKPOINT",
            replays = input.Replays, limitations = new[] { "Repository-owned synthetic driver only.", "Exploitability was not assessed.", "Raw dump withheld by default." }
        }, cancellationToken).ConfigureAwait(false);
        await WriteJson(Path.Combine(fullRoot, "crash", "analysis.json"), analysis, cancellationToken).ConfigureAwait(false);
        await WriteText(Path.Combine(fullRoot, "crash", "windbg.sanitized.txt"), BuildSanitizedWindbg(analysis), cancellationToken).ConfigureAwait(false);
        await WriteJson(Path.Combine(fullRoot, "private-artifacts.json"), new
        {
            dump = new { sha256 = input.DumpSha256, publication = "WITHHELD_SENSITIVE_KERNEL_MEMORY" },
            windbg_raw = new { sha256 = input.RawWindbgSha256, publication = "WITHHELD_PENDING_MANUAL_REDACTION" },
            discovery_journal = new { sha256 = input.DiscoveryJournalSha256, publication = "WITHHELD_CONTAINS_PRIVATE_LAB_METADATA" },
            discovery_watchdog = new { sha256 = input.DiscoveryWatchdogSha256, publication = "WITHHELD_CONTAINS_PRIVATE_LAB_METADATA" }
        }, cancellationToken).ConfigureAwait(false);
        await WriteBytes(Path.Combine(fullRoot, "inputs", "original.case.json"), input.Original.CanonicalUtf8, cancellationToken).ConfigureAwait(false);
        await WriteBytes(Path.Combine(fullRoot, "inputs", "minimized.case.json"), input.Minimized.CanonicalUtf8, cancellationToken).ConfigureAwait(false);
        await WriteText(Path.Combine(fullRoot, "report", "index.html"), BuildReport(input, signature), cancellationToken).ConfigureAwait(false);
        await EvidenceManifest.CreateAsync(fullRoot, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RealLabEvidenceVerification> VerifyAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return new(false, 0, ["Real-lab evidence directory does not exist."]);
        var errors = new List<string>();
        ManifestVerification manifest;
        try { manifest = await EvidenceManifest.VerifyAsync(root, cancellationToken).ConfigureAwait(false); errors.AddRange(manifest.Errors); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { return new(false, 0, [$"Manifest verification failed: {exception.Message}"]); }
        if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(static path => path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Public real-lab bundles must not contain a memory dump.");
        var verifiedPaths = manifest.Verified.Select(static item => item.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "finding.json", "environment.json", "decision.json", "private-artifacts.json", "crash/analysis.json", "crash/windbg.sanitized.txt", "inputs/original.case.json", "inputs/minimized.case.json", "report/index.html" })
            if (!verifiedPaths.Contains(required)) errors.Add($"Required real-lab evidence file is missing: {required}");
        try
        {
            using var finding = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "finding.json"), cancellationToken));
            using var decision = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "decision.json"), cancellationToken));
            using var environment = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "environment.json"), cancellationToken));
            using var privateArtifacts = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "private-artifacts.json"), cancellationToken));
            using var storedAnalysisDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "crash", "analysis.json"), cancellationToken));
            var sanitized = await File.ReadAllTextAsync(Path.Combine(root, "crash", "windbg.sanitized.txt"), cancellationToken);
            var analysis = RealWindbgParser.Parse(sanitized);
            var signature = SignatureV1.Compute(analysis);
            var storedAnalysis = storedAnalysisDocument.RootElement.Deserialize<TriageAnalysis>(ContractJson.Compact)
                ?? throw new InvalidDataException("analysis.json is empty.");
            var original = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "inputs", "original.case.json"), cancellationToken));
            var minimized = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "inputs", "minimized.case.json"), cancellationToken));
            if (finding.RootElement.GetProperty("execution_mode").GetString() != "REAL_LAB") errors.Add("finding execution_mode is not REAL_LAB.");
            if (environment.RootElement.GetProperty("execution_mode").GetString() != "REAL_LAB") errors.Add("environment execution_mode is not REAL_LAB.");
            foreach (var property in new[] { "driver_sha256", "vm_identity_sha256", "checkpoint_identity_sha256", "authorization_sha256", "symbols_sha256" })
                if (!IsHash(environment.RootElement.GetProperty(property).GetString())) errors.Add($"environment {property} is not a lowercase SHA-256.");
            if (!IsHash(privateArtifacts.RootElement.GetProperty("dump").GetProperty("sha256").GetString())
                || !IsHash(privateArtifacts.RootElement.GetProperty("windbg_raw").GetProperty("sha256").GetString())
                || !IsHash(privateArtifacts.RootElement.GetProperty("discovery_journal").GetProperty("sha256").GetString())
                || !IsHash(privateArtifacts.RootElement.GetProperty("discovery_watchdog").GetProperty("sha256").GetString()))
                errors.Add("Private artifact references do not contain valid SHA-256 values.");
            if (finding.RootElement.GetProperty("case_id").GetString() != original.CaseId || finding.RootElement.GetProperty("minimized_case_id").GetString() != minimized.CaseId)
                errors.Add("Finding case identities do not match canonical evidence inputs.");
            if (finding.RootElement.GetProperty("signature").GetString() != signature) errors.Add("Finding signature does not match sanitized WinDbg evidence.");
            if (SignatureV1.Compute(storedAnalysis) != signature) errors.Add("analysis.json does not match sanitized WinDbg evidence.");
            var claims = finding.RootElement.GetProperty("claims");
            if (claims.GetProperty("root_cause").GetString() != "SYNTHETIC_BUGCHECK_ORACLE" || claims.GetProperty("exploitability").GetString() != "NOT_ASSESSED")
                errors.Add("Real-lab claims exceed the repository-owned synthetic target policy.");
            var replays = decision.RootElement.GetProperty("replays").EnumerateArray().ToArray();
            if (decision.RootElement.GetProperty("status").GetString() != "CONFIRMED"
                || decision.RootElement.GetProperty("replay_policy").GetString() != "3/3_COLD_CHECKPOINT")
                errors.Add("Decision status or replay policy is invalid.");
            if (replays.Length != 3
                || !replays.Select(item => item.GetProperty("attempt").GetInt32()).Order().SequenceEqual(new[] { 1, 2, 3 })
                || replays.Any(item => item.GetProperty("classification").GetString() != "MATCH" || item.GetProperty("signature").GetString() != signature))
                errors.Add("Decision does not contain three matching cold replays.");
            if (replays.Any(item => !IsHash(item.GetProperty("dump_sha256").GetString())
                                    || !IsHash(item.GetProperty("journal_sha256").GetString())
                                    || !IsHash(item.GetProperty("watchdog_sha256").GetString())))
                errors.Add("Replay private evidence references contain invalid hashes.");
            var report = await File.ReadAllTextAsync(Path.Combine(root, "report", "index.html"), cancellationToken);
            if (!report.Contains("REAL WINDOWS LAB — REPOSITORY-OWNED SYNTHETIC DRIVER", StringComparison.Ordinal)
                || !report.Contains("Exploitability</dt><dd>NOT ASSESSED", StringComparison.Ordinal))
                errors.Add("Public report is missing mandatory real-lab scope and claims banners.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or KeyNotFoundException)
        { errors.Add($"Semantic verification failed: {exception.Message}"); }
        return new(errors.Count == 0, manifest.Verified.Count, errors);
    }

    private static void ValidateHash(string? value, string name)
    { if (!IsHash(value)) throw new InvalidDataException($"{name} SHA-256 is invalid."); }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(static item => item is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    private static Task WriteJson(string path, object value, CancellationToken token) => WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, ContractJson.Indented), token);
    private static Task WriteText(string path, string value, CancellationToken token) => WriteBytes(path, Encoding.UTF8.GetBytes(value), token);
    private static async Task WriteBytes(string path, ReadOnlyMemory<byte> value, CancellationToken token)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, value.ToArray(), token).ConfigureAwait(false); }

    private static string BuildReport(RealLabEvidenceInput input, string signature) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>KCrashLab confirmed isolated-lab finding</title><style>body{font-family:Segoe UI,system-ui,sans-serif;max-width:920px;margin:40px auto;padding:0 20px;background:#f4f6f8;color:#16202a}.banner{background:#173b57;color:#fff;padding:18px;border-radius:8px;font-weight:700}main{background:#fff;padding:28px;margin-top:18px;border-radius:8px}dt{font-weight:700;margin-top:12px}dd{margin-left:0}code{overflow-wrap:anywhere}</style></head>
        <body><div class="banner">REAL WINDOWS LAB — REPOSITORY-OWNED SYNTHETIC DRIVER</div><main><h1>Confirmed reproducibility finding</h1><dl>
        <dt>Exact signature v1</dt><dd><code>{{WebUtility.HtmlEncode(signature)}}</code></dd>
        <dt>Original case</dt><dd><code>{{WebUtility.HtmlEncode(input.Original.CaseId)}}</code></dd>
        <dt>Minimized case</dt><dd><code>{{WebUtility.HtmlEncode(input.Minimized.CaseId)}}</code></dd>
        <dt>Cold replay</dt><dd>3/3 matching from the pinned immutable checkpoint</dd>
        <dt>Driver</dt><dd><code>{{WebUtility.HtmlEncode(input.DriverSha256)}}</code></dd>
        <dt>Raw dump</dt><dd>WITHHELD — sensitive kernel memory; SHA-256 recorded in private-artifacts.json</dd>
        <dt>Root cause</dt><dd>Repository-owned deterministic synthetic bugcheck oracle</dd>
        <dt>Exploitability</dt><dd>NOT ASSESSED</dd></dl></main></body></html>
        """;

    private static string BuildSanitizedWindbg(TriageAnalysis analysis)
    {
        var parameters = string.Join(", ", analysis.RelevantParameters);
        var frames = string.Join("\n", analysis.NormalizedFrames.Select(static frame => $"00000000`00000000 00000000`00000000 : {frame}"));
        return $"BugCheck {analysis.BugcheckCode}, {{{parameters}}}\n\nMODULE_NAME: {analysis.FaultingModule}\n\nSTACK_TEXT:\n{frames}\n";
    }
}
