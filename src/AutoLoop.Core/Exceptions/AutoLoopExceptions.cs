using AutoLoop.Core.Models;

namespace AutoLoop.Core.Exceptions;

public class AutoLoopException : Exception
{
    public AutoLoopException(string message) : base(message) { }
    public AutoLoopException(string message, Exception inner) : base(message, inner) { }
}

public class NoHypothesisException : AutoLoopException
{
    public NoHypothesisException()
        : base("Aucune hypothèse n'a pu être générée pour ce cycle.") { }
}

public class MutationValidationException : AutoLoopException
{
    public Hypothesis Hypothesis { get; }
    public IReadOnlyList<string> Errors { get; }

    public MutationValidationException(Hypothesis hypothesis, IReadOnlyList<string> errors)
        : base($"La mutation pour l'hypothèse {hypothesis.Id} ne compile pas : {string.Join(", ", errors)}")
    {
        Hypothesis = hypothesis;
        Errors = errors;
    }
}

public class RollbackFailedException : AutoLoopException
{
    public CycleId CycleId { get; }

    public RollbackFailedException(CycleId cycleId, Exception inner)
        : base($"Le rollback du cycle {cycleId} a échoué : {inner.Message}", inner)
    {
        CycleId = cycleId;
    }
}

public class CriticalRollbackFailureException : AutoLoopException
{
    public CycleId CycleId { get; }

    public CriticalRollbackFailureException(CycleId cycleId, string details)
        : base($"ÉCHEC CRITIQUE du rollback pour le cycle {cycleId}. Arrêt du système. Détails : {details}")
    {
        CycleId = cycleId;
    }
}

public class GitHubApiException : AutoLoopException
{
    public string Operation { get; }

    public GitHubApiException(string operation, Exception inner)
        : base($"L'opération GitHub '{operation}' a échoué : {inner.Message}", inner)
    {
        Operation = operation;
    }
}

public class VersioningException : AutoLoopException
{
    public VersioningException(string message) : base(message) { }
    public VersioningException(string message, Exception inner) : base(message, inner) { }
}
