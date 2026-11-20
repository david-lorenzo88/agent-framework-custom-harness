using Microsoft.Agents.AI;

namespace UpgradeCaptain.Harness;

/// <summary>
/// Quién pide el trabajo y cómo se ejecuta. Vive en el StateBag de la sesión, así que viaja con
/// SerializeSessionAsync y sobrevive a reinicios (y a escalar a cero en el Hosted Agent).
/// </summary>
/// <param name="JobId">Identificador del trabajo (p. ej. job-001 o console-20261023-1030).</param>
/// <param name="RequestedBy">Quién lo pide: --as en consola, requestedBy del job en desatendido (@dependabot).</param>
/// <param name="Attended">true en la consola (hay una persona delante); false en run --job y en el Hosted Agent.</param>
public sealed record JobContext(string JobId, string RequestedBy, bool Attended)
{
    public const string StateKey = "upgradeCaptain.job";

    public static JobContext? Get(AgentSession? session) =>
        session is not null && session.StateBag.TryGetValue(StateKey, out JobContext? job) ? job : null;

    public static void Set(AgentSession session, JobContext job) => session.StateBag.SetValue(StateKey, job);

    /// <summary>El trabajo de la ejecución en curso (AIAgent.CurrentRunContext), si lo hay.</summary>
    public static JobContext? Current => Get(AIAgent.CurrentRunContext?.Session);
}
