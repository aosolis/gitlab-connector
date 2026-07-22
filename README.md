# GitLab custom connector for Power Platform

A custom [Power Platform](https://learn.microsoft.com/connectors/custom-connectors/) connector for **GitLab**, usable from Power Automate, Power Apps, and Copilot Studio. It works with **GitLab.com (SaaS)** and **self-managed** GitLab instances via a configurable host.

Authentication is **OAuth 2.0** (authorization code flow) against a GitLab application you register.

## Operations

## Actions

| Operation | Method | Path |
| --- | --- | --- |
| Get current user | GET | `/user` |
| List projects | GET | `/projects` |
| Get project | GET | `/projects/{id}` |
| List issues | GET | `/projects/{id}/issues` |
| Create issue | POST | `/projects/{id}/issues` |
| Get issue | GET | `/projects/{id}/issues/{issue_iid}` |
| Update issue | PUT | `/projects/{id}/issues/{issue_iid}` |
| Add comment to issue | POST | `/projects/{id}/issues/{issue_iid}/notes` |
| List merge requests | GET | `/projects/{id}/merge_requests` |
| Create merge request | POST | `/projects/{id}/merge_requests` |
| List pipelines | GET | `/projects/{id}/pipelines` |
| Trigger pipeline | POST | `/projects/{id}/pipeline` |
| List project hooks | GET | `/projects/{id}/hooks` |
| Delete project hook | DELETE | `/projects/{id}/hooks/{hook_id}` |

## Triggers

All triggers are GitLab **webhook** triggers. Each is defined on its own unique (synthetic) path so OpenAPI's unique `method + path` rule is satisfied, and the `routerequesttoendpoint` policy rewrites every one to the real create-hook endpoint `POST /projects/{id}/hooks`.

| Trigger | Synthetic path | Event flag | Payload schema |
| --- | --- | --- | --- |
| When code is pushed | `/projects/{id}/push_hooks` | `push_events` | `PushEvent` |
| When a tag is pushed | `/projects/{id}/tag_push_hooks` | `tag_push_events` | `TagPushEvent` |
| When an issue is created or updated | `/projects/{id}/issue_hooks` | `issues_events` | `IssueEvent` |
| When a merge request is created or updated | `/projects/{id}/merge_request_hooks` | `merge_requests_events` | `MergeRequestEvent` |
| When a comment is added | `/projects/{id}/note_hooks` | `note_events` | `NoteEvent` |
| When a pipeline status changes | `/projects/{id}/pipeline_hooks` | `pipeline_events` | `PipelineEvent` |
| When a job status changes | `/projects/{id}/job_hooks` | `job_events` | `JobEvent` |
| When a wiki page changes | `/projects/{id}/wiki_page_hooks` | `wiki_page_events` | `WikiPageEvent` |
| When a deployment status changes | `/projects/{id}/deployment_hooks` | `deployment_events` | `DeploymentEvent` |
| When a release is created or updated | `/projects/{id}/release_hooks` | `releases_events` | `ReleaseEvent` |
| When a milestone is created or updated | `/projects/{id}/milestone_hooks` | `milestone_events` | `MilestoneEvent` |
| When a feature flag changes | `/projects/{id}/feature_flag_hooks` | `feature_flag_events` | `FeatureFlagEvent` |
| When a project access token is expiring | `/projects/{id}/access_token_hooks` | `resource_access_token_events` | `AccessTokenEvent` |

How the webhook lifecycle works:

- **Subscribe:** on flow save, the trigger registers a project hook scoped to its single event flag (`push_events` is forced to `false` on all non-push triggers so each hook fires on only its event type). The callback URL is a body property named **`notificationUrl`** (marked `x-ms-notification-url`); `script.csx` renames it to GitLab's expected `url` field before sending.
- **Notify:** GitLab POSTs each event to the callback; downstream steps get typed outputs from the payload schema.
- **Auto-unsubscribe:** GitLab's create-hook response has no `Location` header, so `script.csx` builds one from the created hook's `id` and the connector-facing `OriginalRequestUri`, rewriting the synthetic trailing segment to `/projects/{id}/hooks/{hookId}` (which maps to the `DeleteProjectHook` operation). Keeping the URL on the connector host ensures the platform's unsubscribe DELETE routes back through the connector with the connection's OAuth token; when the flow is turned off, that DELETE removes the hook. (`script.csx` is bound to the trigger operations via `scriptOperations` in `apiProperties.json`.)
- **Confidential/internal items:** the issue and comment triggers expose an **Include confidential issues** / **Include internal comments** toggle (default off) that adds `confidential_issues_events` / `confidential_note_events` to the subscription.
- **Permissions:** managing webhooks requires the **Maintainer** role (or higher) on the project. `List project hooks` / `Delete project hook` actions are provided for manual cleanup.

The GitLab API base path `/api/v4` is applied via the host template in `apiProperties.json` (the `dynamichosturl` policy replaces the host and drops the swagger `basePath`, so `/api/v4` is included in the template; the trigger `newPath` is therefore relative: `/projects/{id}/hooks`). The **project `{id}`** parameter accepts either a numeric ID or a URL-encoded path such as `my-group/my-project`.

## Files

| File | Purpose |
| --- | --- |
| `apiDefinition.swagger.json` | OpenAPI 2.0 (Swagger) definition of the actions and triggers. |
| `apiProperties.json` | Connection parameters, OAuth 2.0 settings, the dynamic-host + webhook-routing policies, and `scriptOperations`. |
| `script.csx` | C# custom code: remaps `notificationUrl`→`url` on subscribe, and sets the `Location` header so triggers auto-unsubscribe. |
| `settings.json` | `paconn` CLI settings. |
| `icon.png` | Connector icon (brand color `#FC6D26`). |

## 1. Register a GitLab OAuth application

Create an OAuth application in GitLab:

- **GitLab.com:** User **Settings → Applications** (or a **Group**/**Admin** application).
- **Self-managed:** the equivalent Applications page on your instance.

Configure it as follows:

- **Redirect URI:** `https://global.consent.azure-apim.net/redirect`
- **Confidential:** Yes
- **Scopes:** `api` (full read/write). Use `read_api` instead if you only need the read operations.

Save the **Application ID** (client ID) and **Secret** (client secret).

## 2. Import the connector

Two options.

### Option A — paconn CLI (recommended)

```powershell
# Install the CLI (Python 3.6+)
pip install paconn

# Sign in to Power Platform
paconn login

# Create the connector from this folder
paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --icon icon.png --secret <YOUR_GITLAB_CLIENT_SECRET>
```

`paconn create` prints a connector ID; save it into `settings.json` (`connectorId` / `environment`) so you can later run `paconn update` to push changes. Before running, set the `clientId` in `apiProperties.json` (replace the `<<Enter your GitLab application (client) ID>>` placeholder), or set it in the connector's **Security** tab after import.

### Option B — Power Platform maker portal

1. Go to [make.powerautomate.com](https://make.powerautomate.com) → **More** → **Discover all** → **Custom connectors** → **New custom connector → Import an OpenAPI file**.
2. Upload `apiDefinition.swagger.json` and set the icon to `icon.png`.
3. On the **Security** tab (OAuth 2.0, Generic OAuth 2), enter:
   - **Client ID / Client secret:** from your GitLab application.
   - **Authorization URL:** `https://gitlab.com/oauth/authorize`
   - **Token URL:** `https://gitlab.com/oauth/token`
   - **Refresh URL:** `https://gitlab.com/oauth/token`
   - **Scope:** `api`
4. Save (**Create connector**), then confirm the redirect URL shown matches the one registered in GitLab.

## 3. Create a connection

When creating a connection you will be asked for:

- **GitLab host** — e.g. `gitlab.com` or `gitlab.mycompany.com` (no `https://`). The `dynamichosturl` policy routes all API calls to this host.
- **OAuth sign-in** — you'll be redirected to GitLab to authorize the application.

## Self-managed GitLab notes

- The **GitLab host** connection parameter already lets API calls target any instance.
- Power Platform requires **static** OAuth authorization/token URLs, so they default to `gitlab.com`. For a self-managed instance, edit the three URL values in `apiProperties.json` (`authorizationUrlTemplate`, `tokenUrlTemplate`, `refreshUrlTemplate`) — or the Security tab after import — to point at your host, e.g. `https://gitlab.mycompany.com/oauth/authorize`. The OAuth host should match the host your tokens are issued from.

## References

- [Setup guide](docs/SETUP_GUIDE.md) — step-by-step from registering the GitLab app through creating a connection and testing a flow.
- [Implementation notes & gotchas](docs/IMPLEMENTATION_NOTES.md) — hard-won details on OAuth, the dynamic-host and webhook-routing policies, `script.csx`, auto-unsubscribe, and certification rules.
- [GitLab REST API](https://docs.gitlab.com/ee/api/)
- [GitLab OAuth 2.0 provider](https://docs.gitlab.com/ee/integration/oauth_provider.html)
- [Custom connectors overview](https://learn.microsoft.com/connectors/custom-connectors/)
- [Set Host URL policy (`dynamichosturl`)](https://learn.microsoft.com/connectors/custom-connectors/policy-templates/dynamichosturl/dynamichosturl)
- [paconn CLI](https://github.com/microsoft/PowerPlatformConnectors/tree/master/tools/paconn-cli)
