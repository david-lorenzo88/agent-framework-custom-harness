# Fallbacks

## Video per block

Record every block at the final rehearsal (1080p, 20 pt terminal). Keep copies on the desktop and on a USB stick. File names: `01-step-1.mp4` … `04-step-4.mp4`.

## Network

- **Phone hotspot first.** Test it at the venue before the session.
- **Second model deployment in another region**, switchable with `.env.alt`:

  ```bash
  cp .env .env.main && cp .env.alt .env     # switch
  scripts/smoke-model.sh                   # confirm
  ```

  `.env.alt` holds `FOUNDRY_PROJECT_ENDPOINT` and `FOUNDRY_MODEL` of the backup deployment (e.g. `gpt-5.4-mini`).

## Hosted

- Deploy **at least 24 hours** before the talk: `bash scripts/deploy-hosted.sh` (about 5 min; it is already deployed as `upgrade-captain` v3 since 2026-10-13). Redeploy only if the code changes.
- Trigger the stage job **at least 30 minutes** before the session (`scripts/hosted.sh trigger`) and confirm `waiting_approval`.
- If Foundry fails, run the host locally and use `--local`:

  ```bash
  scripts/hosted.sh serve --local          # T4, keep it running
  scripts/hosted.sh trigger --local --wait # before the session: parks APR-0001
  scripts/hosted.sh approve APR-0001 --as @maria-payments --local
  ```

  Locally the identity travels in `x-agent-user-id`, simulated from `demo/identities.json`.

## Step 3 · Tu harness

If the model goes off script:
1. `scripts/reset-demo.sh --state after-mechanical-fix` and retry **once**.
2. Then switch to the video.

What usually goes wrong and the quick fix:
- **The agent asks instead of acting:** you are in `plan` mode → `/mode execute`.
- **It edits the tests instead of the code:** the feedback already says "no cambies los tests". Retry once.
- **A second Payments approval appears (APR-0002):** expected (D-11). Approve it the same way.
- **The console "hangs" after ⏸:** it is waiting for María. Use T2.

**Proof without a model.** This is not for the stage; use it to show the mechanism works if asked afterwards:

```bash
UC_INTEGRATION=1 dotnet test tests/UpgradeCaptain.Harness.Tests --filter Step3Beat
```

## CodeAct

Show the measurement log (`docs/verification-log.md`, "Surface measurement") and the video.

## Anything else

`scripts/goto-step.sh N --force` always brings back the exact tag and a fresh demo repo in under 10 seconds once warm.
