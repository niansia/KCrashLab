using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed record CorpusAdmissionContext(
    CanonicalCase TestCase,
    FuzzObservation Observation,
    int Execution,
    int NovelCoverage,
    bool IsSeed);

public interface ICorpusAdmissionPolicy
{
    string PolicyId { get; }

    bool ShouldAdmit(CorpusAdmissionContext context);
}

public sealed record ParentSelectionCandidate(
    string CaseId,
    int Energy,
    int Selections,
    int LastSelectedIteration);

public interface IParentSelectionPolicy
{
    string PolicyId { get; }

    int SelectIndex(
        IReadOnlyList<ParentSelectionCandidate> candidates,
        DeterministicDecisionContext decision);
}

public interface IOperatorSelectionPolicy
{
    string PolicyId { get; }

    int SelectIndex(
        IReadOnlyList<ICaseMutationOperator> operators,
        DeterministicDecisionContext decision);
}

public interface ICandidateSelectionPolicy
{
    string PolicyId { get; }

    int SelectIndex(
        IReadOnlyList<CanonicalCase> candidates,
        DeterministicDecisionContext decision);
}

public readonly record struct DeterministicDecisionContext(long CampaignSeed, int SchedulingIteration, string Lane)
{
    public int Next(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMaximum, 1);
        Span<byte> input = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(input, CampaignSeed);
        BinaryPrimitives.WriteInt32LittleEndian(input[8..], SchedulingIteration);
        BinaryPrimitives.WriteUInt32LittleEndian(input[12..], StableLaneId(Lane));
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        return (int)(BinaryPrimitives.ReadUInt64LittleEndian(digest) % (ulong)exclusiveMaximum);
    }

    private static uint StableLaneId(string lane)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(lane));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}

public sealed class KeepAllCorpusAdmissionPolicy : ICorpusAdmissionPolicy
{
    public string PolicyId => "KEEP_ALL";

    public bool ShouldAdmit(CorpusAdmissionContext context) => true;
}

public sealed class NoveltyOnlyCorpusAdmissionPolicy : ICorpusAdmissionPolicy
{
    public string PolicyId => "NOVELTY_ONLY";

    public bool ShouldAdmit(CorpusAdmissionContext context) =>
        context.IsSeed || context.NovelCoverage > 0 || context.Observation.Signature is not null;
}

public sealed class UniformParentSelectionPolicy : IParentSelectionPolicy
{
    public string PolicyId => "UNIFORM";

    public int SelectIndex(IReadOnlyList<ParentSelectionCandidate> candidates, DeterministicDecisionContext decision) =>
        decision.Next(candidates.Count);
}

public sealed class EnergyParentSelectionPolicy : IParentSelectionPolicy
{
    public string PolicyId => "ENERGY";

    public int SelectIndex(IReadOnlyList<ParentSelectionCandidate> candidates, DeterministicDecisionContext decision)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Cannot select from an empty corpus.");
        }

        return candidates
            .Select(static (candidate, index) => (Candidate: candidate, Index: index))
            .OrderByDescending(static item => (double)item.Candidate.Energy / (item.Candidate.Selections + 1))
            .ThenBy(static item => item.Candidate.LastSelectedIteration)
            .ThenBy(static item => item.Candidate.CaseId, StringComparer.Ordinal)
            .First().Index;
    }
}

public sealed class UniformOperatorSelectionPolicy : IOperatorSelectionPolicy
{
    public string PolicyId => "UNIFORM";

    public int SelectIndex(IReadOnlyList<ICaseMutationOperator> operators, DeterministicDecisionContext decision) =>
        decision.Next(operators.Count);
}

public sealed class UniformCandidateSelectionPolicy : ICandidateSelectionPolicy
{
    public string PolicyId => "UNIFORM";

    public int SelectIndex(IReadOnlyList<CanonicalCase> candidates, DeterministicDecisionContext decision) =>
        decision.Next(candidates.Count);
}

public sealed record FuzzPolicySet(
    ICorpusAdmissionPolicy CorpusAdmission,
    IParentSelectionPolicy ParentSelection,
    IOperatorSelectionPolicy OperatorSelection,
    ICandidateSelectionPolicy CandidateSelection)
{
    public string StrategyId => $"{CorpusAdmission.PolicyId}_{ParentSelection.PolicyId}_V2";

    public static FuzzPolicySet KeepAllUniform() => new(
        new KeepAllCorpusAdmissionPolicy(),
        new UniformParentSelectionPolicy(),
        new UniformOperatorSelectionPolicy(),
        new UniformCandidateSelectionPolicy());

    public static FuzzPolicySet KeepAllEnergy() => new(
        new KeepAllCorpusAdmissionPolicy(),
        new EnergyParentSelectionPolicy(),
        new UniformOperatorSelectionPolicy(),
        new UniformCandidateSelectionPolicy());

    public static FuzzPolicySet NoveltyUniform() => new(
        new NoveltyOnlyCorpusAdmissionPolicy(),
        new UniformParentSelectionPolicy(),
        new UniformOperatorSelectionPolicy(),
        new UniformCandidateSelectionPolicy());

    public static FuzzPolicySet NoveltyEnergy() => new(
        new NoveltyOnlyCorpusAdmissionPolicy(),
        new EnergyParentSelectionPolicy(),
        new UniformOperatorSelectionPolicy(),
        new UniformCandidateSelectionPolicy());
}

public sealed class PolicyDrivenFuzzEngine(
    IReadOnlyList<ICaseMutationOperator> operators,
    FuzzPolicySet policies)
{
    public async Task<FuzzCampaignResult> RunAsync(
        CanonicalCase seed,
        int budget,
        long campaignSeed,
        Func<CanonicalCase, CancellationToken, Task<FuzzObservation>> evaluator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(campaignSeed);
        if (operators.Count == 0)
        {
            throw new InvalidOperationException("At least one mutation operator is required.");
        }

        var corpus = new PolicyCorpus(policies.CorpusAdmission);
        var seenCases = new HashSet<string>(StringComparer.Ordinal) { seed.CaseId };
        var executionLog = new List<FuzzExecutionRecord>(budget);
        var findings = new Dictionary<string, MutableFinding>(StringComparer.Ordinal);
        var mutationContext = new MutationContext(campaignSeed);

        var seedObservation = await evaluator(seed, cancellationToken).ConfigureAwait(false);
        var seedCorpus = corpus.Observe(seed, seedObservation, 1, isSeed: true);
        executionLog.Add(CreateExecutionRecord(1, seed, "seed", seedObservation, seedCorpus));
        ObserveFinding(seed, seedObservation, 1, findings);
        var executions = 1;
        var schedulingIteration = 0;
        var schedulingLimit = Math.Max(4_096, budget * operators.Count * 32);
        var duplicateCandidateSkips = 0;
        var emptyCandidatePolls = 0;

        while (executions < budget && schedulingIteration < schedulingLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            schedulingIteration++;
            var parentIndex = policies.ParentSelection.SelectIndex(
                corpus.ParentCandidates,
                new DeterministicDecisionContext(campaignSeed, schedulingIteration, "parent"));
            var source = corpus.Select(parentIndex, schedulingIteration);
            var operatorIndex = policies.OperatorSelection.SelectIndex(
                operators,
                new DeterministicDecisionContext(campaignSeed, schedulingIteration, "operator"));
            var mutationOperator = operators[operatorIndex];
            var sampledCandidates = mutationOperator.Mutate(source, mutationContext).ToArray();
            var unseenCandidates = sampledCandidates.Where(candidate => !seenCases.Contains(candidate.CaseId)).ToArray();
            duplicateCandidateSkips += sampledCandidates.Length - unseenCandidates.Length;
            if (unseenCandidates.Length == 0)
            {
                emptyCandidatePolls++;
                continue;
            }

            var candidateIndex = policies.CandidateSelection.SelectIndex(
                unseenCandidates,
                new DeterministicDecisionContext(campaignSeed, schedulingIteration, "candidate"));
            var candidate = unseenCandidates[candidateIndex];
            seenCases.Add(candidate.CaseId);
            executions++;
            var observation = await evaluator(candidate, cancellationToken).ConfigureAwait(false);
            var corpusObservation = corpus.Observe(candidate, observation, executions, isSeed: false);
            executionLog.Add(CreateExecutionRecord(
                executions,
                candidate,
                mutationOperator.OperatorId,
                observation,
                corpusObservation));
            ObserveFinding(candidate, observation, executions, findings);
        }

        return new FuzzCampaignResult(
            1,
            "SIMULATED",
            policies.StrategyId,
            campaignSeed,
            budget,
            executions,
            executions == budget ? "BUDGET_REACHED" : "SCHEDULER_ITERATION_LIMIT_REACHED",
            schedulingIteration,
            schedulingLimit,
            duplicateCandidateSkips,
            emptyCandidatePolls,
            MutationCandidateSampling.AlgorithmId,
            mutationContext.MaximumCandidatesPerOperator,
            seed.CaseId,
            corpus.GlobalCoverage.Order(StringComparer.Ordinal).ToArray(),
            corpus.Snapshot(),
            findings.Values
                .OrderBy(static finding => finding.FirstExecution)
                .ThenBy(static finding => finding.Signature, StringComparer.Ordinal)
                .Select(static finding => finding.Snapshot())
                .ToArray(),
            executionLog);
    }

    private static FuzzExecutionRecord CreateExecutionRecord(
        int execution,
        CanonicalCase testCase,
        string operatorId,
        FuzzObservation observation,
        CorpusObservation corpus) =>
        new(execution, testCase.CaseId, testCase.Value.ParentCaseId, operatorId, corpus.NovelCoverage,
            corpus.Added, observation.ResultClass, observation.Signature);

    private static void ObserveFinding(
        CanonicalCase testCase,
        FuzzObservation observation,
        int execution,
        IDictionary<string, MutableFinding> findings)
    {
        if (observation.Signature is null)
        {
            return;
        }

        if (findings.TryGetValue(observation.Signature, out var existing))
        {
            existing.Occurrences++;
            return;
        }

        findings.Add(observation.Signature, new MutableFinding(observation.Signature, testCase.CaseId, execution, testCase));
    }

    private sealed class PolicyCorpus(ICorpusAdmissionPolicy admission)
    {
        private readonly List<MutableCorpusEntry> entries = [];
        private readonly HashSet<string> globalCoverage = new(StringComparer.Ordinal);

        public IReadOnlySet<string> GlobalCoverage => globalCoverage;

        public IReadOnlyList<ParentSelectionCandidate> ParentCandidates => entries
            .Select(static entry => new ParentSelectionCandidate(
                entry.TestCase.CaseId,
                entry.Energy,
                entry.Selections,
                entry.LastSelectedIteration))
            .ToArray();

        public CorpusObservation Observe(
            CanonicalCase testCase,
            FuzzObservation observation,
            int execution,
            bool isSeed)
        {
            var coverage = observation.Coverage.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var novelty = coverage.Count(item => !globalCoverage.Contains(item));
            globalCoverage.UnionWith(coverage);
            var context = new CorpusAdmissionContext(testCase, observation, execution, novelty, isSeed);
            if (!admission.ShouldAdmit(context) || entries.Any(entry => entry.TestCase.CaseId == testCase.CaseId))
            {
                return new CorpusObservation(false, novelty);
            }

            var energy = Math.Max(1, 1 + (novelty * 2) + (observation.Signature is null ? 0 : 8));
            entries.Add(new MutableCorpusEntry(testCase, execution, novelty, energy, coverage));
            return new CorpusObservation(true, novelty);
        }

        public CanonicalCase Select(int index, int iteration)
        {
            var selected = entries[index];
            selected.Selections++;
            selected.LastSelectedIteration = iteration;
            return selected.TestCase;
        }

        public CorpusEntrySnapshot[] Snapshot() => entries
            .OrderBy(static entry => entry.DiscoveryExecution)
            .ThenBy(static entry => entry.TestCase.CaseId, StringComparer.Ordinal)
            .Select(static entry => new CorpusEntrySnapshot(
                entry.TestCase,
                entry.DiscoveryExecution,
                entry.NovelCoverage,
                entry.Energy,
                entry.Selections,
                entry.Coverage))
            .ToArray();
    }

    private sealed class MutableCorpusEntry(
        CanonicalCase testCase,
        int discoveryExecution,
        int novelCoverage,
        int energy,
        IReadOnlyList<string> coverage)
    {
        public CanonicalCase TestCase { get; } = testCase;
        public int DiscoveryExecution { get; } = discoveryExecution;
        public int NovelCoverage { get; } = novelCoverage;
        public int Energy { get; } = energy;
        public IReadOnlyList<string> Coverage { get; } = coverage;
        public int Selections { get; set; }
        public int LastSelectedIteration { get; set; } = -1;
    }

    private sealed class MutableFinding(
        string signature,
        string firstCaseId,
        int firstExecution,
        CanonicalCase representative)
    {
        public string Signature { get; } = signature;
        public string FirstCaseId { get; } = firstCaseId;
        public int FirstExecution { get; } = firstExecution;
        public CanonicalCase Representative { get; } = representative;
        public int Occurrences { get; set; } = 1;
        public FuzzFindingSnapshot Snapshot() =>
            new(Signature, FirstCaseId, FirstExecution, Occurrences, Representative);
    }
}
