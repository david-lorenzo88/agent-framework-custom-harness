//@source src/UpgradeCaptain.Harness/CodeOwnerApprovalAgent.cs
//en: The outermost decorator: it holds back the harness's approval requests and decides where they go.
//es: El decorador más externo: retiene los requests de aprobación del harness y decide adónde van.
public sealed class CodeOwnerApprovalAgent : DelegatingAIAgent //!
{
    // …
    //en: Someone is in front and no protected path → the normal approval prompt.
    //es: Hay una persona delante y ninguna ruta protegida → la aprobación de siempre.
    if (job.Attended && !held.Any(this.TouchesProtectedPath)) //!
    {
        await this.PersistAsync(job, session, cancellationToken).ConfigureAwait(false);
        yield return new AgentResponseUpdate(ChatRole.Assistant, [.. held]);
        yield break;
    }

    //en: Otherwise it is PARKED: ApprovalStore + SessionVault + AuditLog, and the session doesn't move on.
    //es: Si no, se APARCA: ApprovalStore + SessionVault + AuditLog, y la sesión no avanza.
    IReadOnlyList<PendingApproval> parked = await this.ParkAsync(job, session, held, cancellationToken).ConfigureAwait(false); //!
    // …
    //en: The decision rules (DecideAsync):
    //es: Las reglas de la decisión (DecideAsync):
    //en: 1. The approver must own (CODEOWNERS) EVERY touched path.
    //es: 1. Debe ser propietario (CODEOWNERS) de CADA ruta tocada.
    var notOwned = approval.Paths.Where(p => !this._services.Policy.IsOwner(approverId, p)).ToList(); //!
    // …
    //en: 2. Separation of duties: whoever asked for the job can't approve their own changes.
    //es: 2. Separación de funciones: quien pide el trabajo no aprueba sus propios cambios.
    if (string.Equals(approverId, approval.RequestedBy, StringComparison.OrdinalIgnoreCase)) //!
    // …
}
