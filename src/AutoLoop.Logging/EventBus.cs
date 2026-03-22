using AutoLoop.Core.Models;

namespace AutoLoop.Logging;

/// <summary>
/// Bus d'événements pub/sub interne. Découple les modules producteurs des consommateurs.
/// </summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : class;
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}

public sealed class InMemoryEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_lock)
        {
            var type = typeof(TEvent);
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _handlers[type] = list;
            }
            list.Add(handler);
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_lock)
        {
            var type = typeof(TEvent);
            if (_handlers.TryGetValue(type, out var list))
                list.Remove(handler);
        }
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        List<Delegate>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out handlers))
                return;
            handlers = new List<Delegate>(handlers); // copie pour éviter deadlock
        }

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler)(@event);
            }
            catch
            {
                // Les handlers ne doivent pas faire planter le bus
            }
        }
    }
}

// ── Événements du domaine ─────────────────────────────────────────────────────

public sealed record CycleStartedEvent(CycleId CycleId, DateTimeOffset StartedAt);
public sealed record CycleCompletedEvent(CycleId CycleId, CycleStatus Status, TimeSpan Duration);
public sealed record HypothesisGeneratedEvent(CycleId CycleId, int Count, string TopType);
public sealed record MutationAppliedEvent(CycleId CycleId, ChangeRecord Change);
public sealed record TestsCompletedEvent(CycleId CycleId, bool AllPassed, int TotalTests, int Failed);
public sealed record DecisionMadeEvent(CycleId CycleId, DecisionOutcome Decision, double ImprovementScore, string Rationale);
public sealed record RollbackExecutedEvent(CycleId CycleId, RollbackResult Result);
public sealed record PullRequestCreatedEvent(CycleId CycleId, string PrUrl);
public sealed record AlertFiredEvent(AlertSeverity Severity, string Title, string Message);
