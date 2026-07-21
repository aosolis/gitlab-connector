# GitLab custom connector for Power Platform

A custom [Power Platform](https://learn.microsoft.com/connectors/custom-connectors/) connector for **GitLab**, usable from Power Automate, Power Apps, and Copilot Studio. It works with **GitLab.com (SaaS)** and **self-managed** GitLab instances via a configurable host.

Authentication is **OAuth 2.0** (authorization code flow) against a GitLab application you register.

## Operations

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

The GitLab base path is `/api/v4`. The **project `{id}`** parameter accepts either a numeric ID or a URL-encoded path such as `my-group/my-project`.

## Files

| File | Purpose |
| --- | --- |
| `apiDefinition.swagger.json` | OpenAPI 2.0 (Swagger) definition of the operations. |
| `apiProperties.json` | Connection parameters, OAuth 2.0 settings, and the dynamic-host policy. |
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

- [GitLab REST API](https://docs.gitlab.com/ee/api/)
- [GitLab OAuth 2.0 provider](https://docs.gitlab.com/ee/integration/oauth_provider.html)
- [Custom connectors overview](https://learn.microsoft.com/connectors/custom-connectors/)
- [Set Host URL policy (`dynamichosturl`)](https://learn.microsoft.com/connectors/custom-connectors/policy-templates/dynamichosturl/dynamichosturl)
- [paconn CLI](https://github.com/microsoft/PowerPlatformConnectors/tree/master/tools/paconn-cli)
