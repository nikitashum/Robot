# Cheap, capped deployment on Azure

> **Verify every price and quota below on the Azure pricing pages before relying on it.** They change, and this
> file was written without access to your subscription.

## Decide first: do you even need a cloud?

For "send a link to 3-10 people for a week", a cloud is optional:

| Option | Cost | Always on | Effort | Use when |
|---|---|---|---|---|
| **Local + Cloudflare Tunnel** (`scripts/run-demo.ps1`) | 0 | only while your PC is on | minutes | live pitch, a few testers |
| **Azure Container Apps, scale to zero** (recommended) | ~0 within the monthly free grant | no (cold start of a few seconds) | ~30 min | a link that works while you sleep |
| **Azure App Service F1** | 0 | no, 60 CPU-min/day quota | ~30 min | you want a *hard* usage ceiling |

I recommend Container Apps. App Service F1 is the fallback if you want the platform itself to stop the app instead of billing you.

## Free-credit reality check (be honest with yourself)

- New accounts get a credit of roughly 200 USD for 30 days (verify). **After that the resources are disabled unless you
  upgrade to pay-as-you-go.** If you never upgrade, you cannot be billed: that is your only true hard spending cap.
- Pay-as-you-go **budgets only send alerts; they do not stop anything.** Do not rely on a budget as a cap.
- Azure Container Registry (Basic, ~5 USD/month) would eat 1/40 of your credit for nothing. Use GitHub Container
  Registry (GHCR) instead (below).
- The `Stub` assistant needs no paid service. A real LLM (Azure OpenAI needs access approval; OpenAI is separate
  billing) is the only thing that can cost real money, and it is capped by `Llm__DailyRequestLimit`.

## Hard requirements of this app (do not skip)

1. **Exactly one replica** (`--max-replicas 1`). Sessions and rate limits live in memory. With 2+ replicas a follow-up
   message can land on an instance that has never seen the session, and rate limits multiply.
   Scaling out later means moving `ISessionStore` to Redis first.
2. **No secrets in the image or in git.** API keys go in Container Apps *secrets*, referenced by env var.
3. The app is anonymous and public. It is protected by: per-IP rate limit (20 chat requests/min), global concurrency
   cap (16), 16 KB body limit, and a daily LLM request cap (default 300). Keep all of them.

## 1. Build and publish the image to GHCR

CI already builds the Dockerfile. `.github/workflows/publish-image.yml` pushes it to `ghcr.io/<owner>/butikkpilot`
when you push a `v*` tag (or run the workflow manually). The demo catalog and prompts contain no secrets, so a public
package is acceptable and avoids registry credentials: after the first push, set the package visibility to *public*
in GitHub (Packages -> butikkpilot -> Package settings). Prefer private? Create a read-only PAT (`read:packages`) and
pass it with `--registry-server ghcr.io --registry-username <user> --registry-password <pat>`.

Local test first (Docker Desktop must be running):

```powershell
docker build -t butikkpilot .
docker run --rm -p 8080:8080 butikkpilot   # http://localhost:8080
```

## 2. Create the Container App

```powershell
az login
$rg = "rg-butikkpilot"; $loc = "westeurope"      # pick a region close to your testers; check Container Apps availability
az group create -n $rg -l $loc
az containerapp env create -n cae-butikkpilot -g $rg -l $loc

az containerapp create -n butikkpilot -g $rg --environment cae-butikkpilot `
  --image ghcr.io/<owner>/butikkpilot:v1 `
  --target-port 8080 --ingress external `
  --min-replicas 0 --max-replicas 1 `
  --cpu 0.25 --memory 0.5Gi `
  --env-vars Llm__Provider=Stub Llm__DailyRequestLimit=300 RateLimit__ChatPerMinute=20
```

The command prints the public `https://....azurecontainerapps.io` URL. HTTPS is terminated by the platform;
the app trusts the platform's `X-Forwarded-*` headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED` is set in the image).
Do not expose the container port by any other path, or clients could spoof their IP and dodge the rate limit.

Cold start: with scale-to-zero the first request after idle waits a few seconds. Open the link yourself a minute before
you send it to someone. If that is unacceptable use `--min-replicas 1` and accept the always-on cost (verify the
price; it can exceed the free grant).

## 3. Optional: a real LLM

```powershell
az containerapp secret set -n butikkpilot -g $rg --secrets llm-key=<key>
az containerapp update -n butikkpilot -g $rg --set-env-vars `
  Llm__Provider=AzureOpenAI Llm__Endpoint=https://<resource>.openai.azure.com `
  Llm__Model=<deployment-name> Llm__ApiKey=secretref:llm-key Llm__DailyRequestLimit=200
```

(`Llm__Provider=OpenAI` with `Llm__Model` and `Llm__ApiKey` for plain OpenAI.) Startup fails fast on a wrong or missing
setting; it never silently falls back to the stub, so check `/health` (`llmProvider`) after a change.
When the daily cap is hit the app answers with a polite "service busy" message instead of calling the LLM.
Remember the privacy side: customer text is sent to the LLM provider. Keep the demo's "no real customer data" notice.

## 4. Cost guard rails

- Portal -> Cost Management -> Budgets: monthly budget (e.g. 5 USD) with alerts at 50 / 80 / 100 %. Alerts only.
- Never upgrade the free account to pay-as-you-go while testing (see above).
- Delete everything when the pilot is over: `az group delete -n rg-butikkpilot --yes`.

## 5. Operations

- Health: `GET /health` (`status`, `llmProvider`, `products`). No secrets are exposed.
- Logs: `az containerapp logs show -n butikkpilot -g $rg --follow`. Analytics events are structured log lines
  (no message text, no handoff free text is stored).
- Update: push a new `v*` tag, then `az containerapp update -n butikkpilot -g $rg --image ghcr.io/<owner>/butikkpilot:<tag>`.
  Use immutable tags, not `latest`.
- Dependabot and the weekly CI run fail on vulnerable NuGet packages; .NET 10 is an LTS release (support into 2028; verify).

## Alternative: App Service F1 (hard ceiling, no container)

Free tier, 60 CPU-minutes/day (the site stops when exceeded), no always-on, shared CPU, limited features (verify).
Deploy the folder from `scripts/publish.ps1` (framework-dependent, Linux or Windows plan, .NET 10 runtime) with
`az webapp deploy --type zip`. Same app settings as above (`Llm__Provider`, ...). Still a single instance only.
