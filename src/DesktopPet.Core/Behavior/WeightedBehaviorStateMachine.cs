namespace DesktopPet.Core.Behavior;

/// <summary>
/// Monotonic-time behavior state machine with deterministic weighted selection, cooldowns,
/// interruptibility, and priority arbitration.
/// </summary>
public sealed class WeightedBehaviorStateMachine
{
    private readonly IReadOnlyDictionary<string, BehaviorStateDefinition> _definitions;
    private readonly Dictionary<string, long> _cooldownUntilMs = new(StringComparer.Ordinal);
    private readonly DeterministicRandom _random;
    private long _lastObservedMs;
    private int _activePriority;

    public WeightedBehaviorStateMachine(
        IEnumerable<BehaviorStateDefinition> definitions,
        string initialStateId,
        ulong seed,
        long startedAtMs = 0)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialStateId);

        var stateMap = new Dictionary<string, BehaviorStateDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ValidateDefinition(definition);
            if (!stateMap.TryAdd(definition.Id, definition))
            {
                throw new BehaviorDefinitionException($"Duplicate state id '{definition.Id}'.");
            }
        }

        if (stateMap.Count == 0)
        {
            throw new BehaviorDefinitionException("At least one behavior state is required.");
        }

        foreach (var definition in stateMap.Values)
        {
            if (definition.NextStateIds is null)
            {
                throw new BehaviorDefinitionException($"State '{definition.Id}' next-state collection cannot be null.");
            }

            foreach (var nextStateId in definition.NextStateIds)
            {
                if (!stateMap.ContainsKey(nextStateId))
                {
                    throw new BehaviorDefinitionException(
                        $"State '{definition.Id}' references unknown next state '{nextStateId}'.");
                }
            }
        }

        if (!stateMap.ContainsKey(initialStateId))
        {
            throw new BehaviorDefinitionException($"Initial state '{initialStateId}' is not defined.");
        }

        if (startedAtMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAtMs));
        }

        _definitions = stateMap;
        _random = new DeterministicRandom(seed);
        _lastObservedMs = startedAtMs;
        CurrentStateId = initialStateId;
        EnteredAtMs = startedAtMs;
        _activePriority = stateMap[initialStateId].Priority;
    }

    public string CurrentStateId { get; private set; }

    public BehaviorStateDefinition Current => _definitions[CurrentStateId];

    public long EnteredAtMs { get; private set; }

    public long ElapsedInCurrentStateMs => _lastObservedMs - EnteredAtMs;

    public IReadOnlyDictionary<string, BehaviorStateDefinition> Definitions => _definitions;

    public bool IsCoolingDown(string stateId, long nowMs)
    {
        EnsureKnownState(stateId);
        ObserveTime(nowMs);
        return _cooldownUntilMs.TryGetValue(stateId, out var untilMs) && nowMs < untilMs;
    }

    /// <summary>Attempts an explicit transition without consuming the random sequence.</summary>
    public TransitionDecision RequestTransition(TransitionRequest request, long nowMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetStateId);
        ObserveTime(nowMs);
        EnsureKnownState(request.TargetStateId);

        if (request.TargetStateId == CurrentStateId && !request.RestartIfCurrent)
        {
            return TransitionDecision.Reject("The requested state is already active.");
        }

        if (IsOnCooldownWithoutObserving(request.TargetStateId, nowMs))
        {
            return TransitionDecision.Reject("The requested state is cooling down.");
        }

        var currentComplete = nowMs - EnteredAtMs >= Current.MinimumDurationMs;
        if (!currentComplete)
        {
            if (request.EffectivePriority <= _activePriority)
            {
                return TransitionDecision.Reject("The request priority does not exceed the active transition priority.");
            }

            // Explicit input is arbitrated by priority even when the coarse behavior state is
            // marked non-interruptible. Visual cue interruptibility belongs to the timeline/UI
            // layer; otherwise a direct Heart/Waving reaction could incorrectly block Drag,
            // Menu, or System requests from the documented priority hierarchy.
        }

        return TransitionDecision.Accept(TransitionTo(
            request.TargetStateId,
            request.Cause,
            request.EffectivePriority,
            nowMs));
    }

    /// <summary>
    /// Selects an eligible next state. Selection is limited to the current state's explicit edges
    /// when present; otherwise every defined state is considered.
    /// </summary>
    public TransitionDecision Advance(long nowMs, int minimumPriority = int.MinValue)
    {
        ObserveTime(nowMs);
        if (nowMs - EnteredAtMs < Current.MinimumDurationMs)
        {
            return TransitionDecision.Reject("The current state's minimum duration has not elapsed.");
        }

        var candidateIds = Current.NextStateIds.Count > 0
            ? Current.NextStateIds
            : _definitions.Keys.ToArray();

        var candidates = candidateIds
            .Distinct(StringComparer.Ordinal)
            .Where(id => id != CurrentStateId)
            .Select(id => _definitions[id])
            .Where(definition => definition.Priority >= minimumPriority)
            .Where(definition => !IsOnCooldownWithoutObserving(definition.Id, nowMs))
            .Where(definition => definition.Weight > 0)
            .ToArray();

        if (candidates.Length == 0)
        {
            return TransitionDecision.Reject("No eligible next state is available.");
        }

        // Priorities arbitrate first; weights only compete within the winning priority band.
        var winningPriority = candidates.Max(candidate => candidate.Priority);
        var weighted = candidates
            .Where(candidate => candidate.Priority == winningPriority)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
        var selected = SelectWeighted(weighted);

        return TransitionDecision.Accept(TransitionTo(
            selected.Id,
            TransitionCause.Scheduled,
            selected.Priority,
            nowMs));
    }

    private BehaviorStateDefinition SelectWeighted(IReadOnlyList<BehaviorStateDefinition> candidates)
    {
        var total = 0d;
        foreach (var candidate in candidates)
        {
            total += candidate.Weight;
            if (!double.IsFinite(total))
            {
                throw new InvalidOperationException("The combined behavior weight is too large.");
            }
        }

        var roll = _random.NextUnitDouble() * total;
        var cumulative = 0d;
        foreach (var candidate in candidates)
        {
            cumulative += candidate.Weight;
            if (roll < cumulative)
            {
                return candidate;
            }
        }

        // Floating-point rounding can only place the roll at the final boundary.
        return candidates[^1];
    }

    private BehaviorTransition TransitionTo(
        string targetStateId,
        TransitionCause cause,
        int effectivePriority,
        long nowMs)
    {
        var previous = Current;
        if (previous.CooldownMs > 0)
        {
            _cooldownUntilMs[previous.Id] = SaturatingAdd(nowMs, previous.CooldownMs);
        }

        CurrentStateId = targetStateId;
        EnteredAtMs = nowMs;
        _activePriority = Math.Max(effectivePriority, _definitions[targetStateId].Priority);
        return new BehaviorTransition(previous.Id, targetStateId, nowMs, cause, _activePriority);
    }

    private void ObserveTime(long nowMs)
    {
        if (nowMs < _lastObservedMs)
        {
            throw new ArgumentOutOfRangeException(nameof(nowMs), "State-machine time must be monotonic.");
        }

        _lastObservedMs = nowMs;
    }

    private bool IsOnCooldownWithoutObserving(string stateId, long nowMs) =>
        _cooldownUntilMs.TryGetValue(stateId, out var untilMs) && nowMs < untilMs;

    private void EnsureKnownState(string stateId)
    {
        if (!_definitions.ContainsKey(stateId))
        {
            throw new KeyNotFoundException($"Unknown state '{stateId}'.");
        }
    }

    private static void ValidateDefinition(BehaviorStateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new BehaviorDefinitionException("State id is required.");
        }

        if (definition.MinimumDurationMs < 0 || definition.CooldownMs < 0)
        {
            throw new BehaviorDefinitionException($"State '{definition.Id}' contains a negative duration.");
        }

        if (!double.IsFinite(definition.Weight) || definition.Weight < 0)
        {
            throw new BehaviorDefinitionException($"State '{definition.Id}' has an invalid weight.");
        }

        if (definition.NextStateIds is null)
        {
            throw new BehaviorDefinitionException($"State '{definition.Id}' next-state collection cannot be null.");
        }

        if (definition.NextStateIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new BehaviorDefinitionException($"State '{definition.Id}' has a blank next-state id.");
        }
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
