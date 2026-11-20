//@source src/UpgradeCaptain.Hosted/UpgradeInvocationHandler.cs
//en: Three events over one webhook.
//es: Tres eventos sobre un único webhook.
switch (type)
{
    case "upgrade.requested": //!
        UpgradeRequest? upgrade = body.Deserialize<UpgradeRequest>();
        // …
        JobStatus started = await jobs.RequestAsync(upgrade, wait);
        // …
    case "approval.decided": //!
        //en: Who approves: x-agent-user-id (Foundry, protocol 2.0.0) → identities.json. The payload only in dev mode.
        //es: Quién aprueba: x-agent-user-id (Foundry, protocolo 2.0.0) → identities.json. El payload, solo en modo desarrollo.
        (string? approver, string? identityError) = identities.Resolve(context.PlatformContext.UserIdKey, Get(body, "approver"));
        // …
    case "job.status": //!
        JobStatus current = jobs.Status(jobId!);
        // …
}
