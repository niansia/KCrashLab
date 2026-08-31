using System.Text.Json;

namespace KCrashLab.Contracts;

public sealed record MutationLineage(string OperatorId, IReadOnlyDictionary<string, JsonElement> Parameters);

public sealed record CaseOperation(
    string Ioctl,
    string? Input,
    IReadOnlyDictionary<string, JsonElement>? Fields);

public sealed record CaseSchedule(int Workers, IReadOnlyList<long> DelaysUs);

public sealed record TestCase(
    int SchemaVersion,
    string Target,
    long Seed,
    IReadOnlyList<CaseOperation> Operations,
    CaseSchedule? Schedule,
    string? ParentCaseId,
    MutationLineage? Mutation);

public sealed record CanonicalCase(TestCase Value, string CaseId, byte[] CanonicalUtf8);

