# GitLab connector — implementation notes & gotchas

This document captures the non-obvious things we discovered while building this
Power Platform custom connector for GitLab. Each item is a real problem we hit,
the symptom, the root cause, and the fix — so future changes don't reintroduce them.

## Contents

- [OAuth 2.0 (generic provider)](#oauth-20-generic-provider)
- [Fixed host & the dynamic-host policy](#fixed-host--the-dynamic-host-policy)
- [Webhook triggers on a single backend endpoint](#webhook-triggers-on-a-single-backend-endpoint)
- [The callback URL property (`notificationUrl`)](#the-callback-url-property-notificationurl)
- [Custom code (`script.csx`) — what policies can't do](#custom-code-scriptcsx--what-policies-cant-do)
- [Auto-unsubscribe (deleting the hook)](#auto-unsubscribe-deleting-the-hook)
- [Swagger certification rules that bit us](#swagger-certification-rules-that-bit-us)
- [Portal Swagger caching](#portal-swagger-caching)
- [Operational tips](#operational-tips)

---

## OAuth 2.0 (generic provider)

**Use `identityProvider: "oauth2generic"` and the `*Template` parameter keys.**

For a generic OAuth2 provider, `apiProperties.json` `customParameters` must use:

```json
"authorizationUrlTemplate": { "value": "https://gitlab.com/oauth/authorize" },
"tokenUrlTemplate":         { "value": "https://gitlab.com/oauth/token" },
"refreshUrlTemplate":       { "value": "https://gitlab.com/oauth/token" }
```

- **Symptom when wrong:** using AAD-style keys (`authorizationUrl` / `tokenUrl` /
  `refreshUrl`) leaves the authorization URL unset. The connection fails
  *before* any GitLab screen appears — Power Platform never redirects to GitLab.
- GitLab endpoints: authorize `…/oauth/authorize`, token & refresh `…/oauth/token`.
- The GitLab OAuth application must be **Confidential** (Power Platform's generic
  OAuth2 does not use PKCE). Scope: `api` (or `read_api` for read-only).

**Redirect URI is per-connector.** After the connector exists, the Security tab
shows a redirect URL like:

```
https://global.consent.azure-apim.net/redirect/gitlab-<suffix>
```

The GitLab application's **Redirect URI** must match this **exactly** (no trailing
slash/space). "The redirect URI included is not valid" means a mismatch. Note the
suffix is tied to the connector — recreating the connector changes it.

**Static OAuth URLs vs. self-managed.** Power Platform requires the
authorization/token URLs to be static (set at design time), so they can't be
driven by the host connection parameter. They default to `gitlab.com`. For a
self-managed instance, edit the three `*UrlTemplate` values (or the Security tab)
to point at that host.

---

## Fixed host & the dynamic-host policy

**Fold the API base path into the host template.** The `dynamichosturl` policy
replaces the host **and drops the swagger `basePath`**. So `/api/v4` must live in
the template, not in `basePath`:

```json
{ "templateId": "dynamichosturl",
  "parameters": { "x-ms-apimTemplateParameter.urlTemplate":
    "https://gitlab.com/api/v4" } }
```

with `basePath: "/"` in the swagger.

- **Symptom when wrong:** with `/api/v4` only in `basePath`, requests went to
  `https://<host>/user` instead of `https://<host>/api/v4/user`. GitLab returned
  an HTML page, and the flow failed with:
  `The API operation 'GetCurrentUser' requires the property 'body' to be of type
  'Object' but is of type 'String'.`
- **Why the host is hardcoded, not a connection parameter.** Under OAuth the
  authorization/token URLs are static (design-time) and the API host must match
  the token issuer — so a per-connection host adds no flexibility: changing hosts
  already requires editing `apiProperties.json` (the OAuth URLs) and redeploying.
  For a self-managed instance, edit the `dynamichosturl` host and the three OAuth
  URLs together. A per-connection host parameter only pays off with PAT auth
  (no static OAuth URLs), which this connector doesn't use.

---

## Webhook triggers on a single backend endpoint

All GitLab project webhooks are created at the **same** endpoint —
`POST /projects/:id/hooks` — differing only by body flags (`issues_events`,
`merge_requests_events`, `pipeline_events`, …). But OpenAPI 2.0 / Power Platform
require **`method + path` to be unique**, and query strings do **not**
disambiguate paths.

**Solution: synthetic paths + the `routerequesttoendpoint` policy.** Each trigger
gets its own unique (synthetic) swagger path, and one policy rewrites them all
back to the real endpoint:

```json
{ "templateId": "routerequesttoendpoint",
  "parameters": {
    "x-ms-apimTemplateParameter.newPath": "/projects/{id}/hooks",
    "x-ms-apimTemplateParameter.httpMethod": "@Request.OriginalHTTPMethod",
    "x-ms-apimTemplate-operationName": [ "OnPushEvent", "OnIssueEvent", … ] } }
```

Key points:

- `newPath` supports `{pathParam}` substitution (e.g. `{id}`).
- `newPath` is **relative to the `dynamichosturl` base**, which already includes
  `/api/v4` — so it's `/projects/{id}/hooks`, **not** `/api/v4/projects/{id}/hooks`.
- Each synthetic trigger declares its own typed `x-ms-notification-content`
  payload schema, so downstream flow steps get strongly-typed outputs.
- Non-push triggers set `push_events: false` so each hook fires on only its event.

**Event coverage.** All 13 settable project webhook event types are implemented:
push, tag_push, issues, merge_requests, note (comments), pipeline, job,
wiki_page, deployment, releases, milestone, feature_flag,
resource_access_token. Group webhooks and system hooks are a different scope
(`/groups/:id/hooks`, `/hooks`) and are intentionally **not** included — this
connector is project-scoped.

**Confidential/internal items.** GitLab fires confidential issues and internal
notes on **separate** flags (`confidential_issues_events`,
`confidential_note_events`). The issue and comment triggers expose an
"Include confidential/internal" boolean (default off, opt-in) that adds the
corresponding flag at subscribe time. It's a subscribe-time setting, not a live
per-event filter — toggling it re-registers the hook.

---

## The callback URL property (`notificationUrl`)

**Name the callback-URL body property `notificationUrl`.** This is the property
marked `x-ms-notification-url: true` that Power Platform fills with the flow's
callback URL. Designer surfaces treat this URL inconsistently under other names,
so `notificationUrl` is the reliable choice.

GitLab's create-hook API expects the field to be named **`url`**, so `script.csx`
renames `notificationUrl` → `url` on the request before sending. (Policies can't
rewrite the body — see below — so this must be done in custom code.)

---

## Custom code (`script.csx`) — what policies can't do

**Policy templates cannot read or modify the request/response body.** Custom
connector policy expressions only expose `@headers()`, `@queryParameters()`, and
`@connectionParameters()` — there is no raw APIM policy XML and no access to
`context.Response.Body`. Anything that needs the body must be done in custom code
(`script.csx`).

We use `script.csx` for two things, both on the webhook subscribe operations:

1. Rename `notificationUrl` → `url` on the request body.
2. Set the `Location` response header for auto-unsubscribe (see next section).

**Binding the script.** `script.csx` is attached to specific operations via
`scriptOperations` in `apiProperties.json` (the list of the 13 trigger
operation IDs). Confirm custom code is enabled on the connector's **Code** tab
after import.

---

## Auto-unsubscribe (deleting the hook)

Power Platform unsubscribes a webhook by issuing an HTTP **DELETE** to the URL in
the subscribe response's **`Location` header**. GitLab's create-hook response
returns the hook `id` but **no `Location` header**, so we synthesize one in
`script.csx`.

**The Location must be built from the connector-facing URI, not the backend host.**

- **Symptom when wrong:** building `Location` as `https://gitlab.com/api/v4/…`
  (the real backend) meant the platform's unsubscribe DELETE went straight to
  GitLab **without the OAuth token**. GitLab rejected it, and the hook was
  **orphaned** — it stayed active after the flow was turned off.
- **Fix:** build `Location` from `this.Context.OriginalRequestUri` (the
  connector/APIM-facing URL). Because our subscribe path is a synthetic
  `…/projects/{id}/<event>_hooks`, we strip the trailing segment and append
  `hooks/{hookId}`, producing `…/projects/{id}/hooks/{hookId}` — which maps to the
  `DeleteProjectHook` operation. Staying on the connector host means the DELETE
  routes back through the connector, carrying the connection's OAuth token and the
  dynamic-host policy, so the hook is actually deleted.

This mirrors Microsoft's certified **MoreApp Forms** connector, which builds its
Location from `Context.OriginalRequestUri` plus the created resource's `id`.

`List project hooks` / `Delete project hook` actions are provided for manual
verification and cleanup.

---

## Swagger certification rules that bit us

Power Platform's `SwaggerCertificationFailedWithErrors` validation is stricter
than plain Swagger 2.0. The errors that blocked us:

1. **`x-ms-notification-content` must be at the path-item level**, i.e. a sibling
   of the HTTP method — **not** inside the operation.
   - Symptom: `The operation '<id>' is missing notification content extension as
     it has '1' properties marked as notification URL.` — even though the
     extension was present (just nested one level too deep).
   - Correct shape:
     ```json
     "/projects/{id}/issue_hooks": {
       "x-ms-notification-content": { "schema": { "$ref": "#/definitions/IssueEvent" } },
       "post": { … }
     }
     ```

2. **`default` responses must not have a `schema`.** Schemas are only allowed on
   expected (explicit) status codes.
   - Symptom: `The 'default' response should not have schema definition.`
   - Fix: `"default": { "description": "Error" }` (no schema). We dropped the
     now-unreferenced `Error` definition.

3. **Internal properties with a default value must be `required`.** Optional
   internal fields are ignored, so an internal-with-default field that isn't
   required won't be sent.
   - Symptom: `The property is internal and has a default value, it must be
     required. Optional internal fields are ignored.`
   - Fix: add the internal trigger-body fields (event flags, `push_events`,
     `enable_ssl_verification`) to the schema's `required` array.

4. **`x-ms-connector-metadata` is required** at the swagger root (website, privacy
   policy, categories).

5. **Path parameters should set `x-ms-url-encoding: single`** (warning). We set it
   on `id`, `issue_iid`, and `hook_id`.

---

## Portal Swagger caching

Changes to the swagger definition can take a **long time** (minutes to hours) to
propagate into the Power Automate designer. Practical consequences:

- Define everything you can in **one** deployment rather than iterating in small
  swagger changes — this is why all 13 triggers were added at once.
- After a definition change, a trigger may still load the **old** cached shape.
  A "flow save failed … must be of type `OpenApiConnectionWebhook`" error usually
  means the designer stamped the trigger from a stale (non-webhook) definition;
  create a **fresh** flow and re-add the trigger once propagation completes.
- `script.csx` changes are **runtime**, not designer-cached, so unsubscribe/body
  fixes take effect on the next subscribe without waiting for the cache.

---

## Operational tips

**Deploy / update:**

```powershell
cd C:\src\gitlab-connector
paconn update -s settings.json --secret <YOUR_GITLAB_CLIENT_SECRET>
```

`settings.json` records the connector ID and environment (written by
`paconn create`), so `update` needs only `-s settings.json` plus the secret.

**Inspect active hooks in GitLab** (PAT with scope `api`):

```powershell
$token = "<your-gitlab-PAT>"
$id = [uri]::EscapeDataString("my-group/my-project")   # or numeric project ID
Invoke-RestMethod "https://gitlab.com/api/v4/projects/$id/hooks" `
  -Headers @{ "PRIVATE-TOKEN" = $token } | ConvertTo-Json -Depth 5
# delete one:
Invoke-RestMethod -Method Delete "https://gitlab.com/api/v4/projects/$id/hooks/<HOOK_ID>" `
  -Headers @{ "PRIVATE-TOKEN" = $token }
```

Or use the connector's **List project hooks** / **Delete project hook** actions,
or the GitLab UI at **Project → Settings → Webhooks**.

**Verifying the webhook lifecycle end-to-end:**

1. Turn the flow **on** → a hook appears in `List project hooks`.
2. Trigger the event in GitLab (e.g. create an issue) → the flow runs.
3. Turn the flow **off** → the hook disappears (auto-unsubscribe).

**Permissions:** managing project webhooks requires the **Maintainer** role (or
higher) on the project.
