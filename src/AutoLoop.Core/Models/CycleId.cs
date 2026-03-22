namespace AutoLoop.Core.Models;

/// <summary>
/// Identifiant immuable d'un cycle. Génère aussi le nom de branche GitHub.
/// </summary>
public sealed record CycleId(Guid Value)
{
    public static CycleId New() => new(Guid.NewGuid());

    /// <summary>Nom de la branche GitHub associée à ce cycle.</summary>
    public string BranchName => $"auto-loop/cycle-{Value:N}";

    public override string ToString() => Value.ToString("N");
}
