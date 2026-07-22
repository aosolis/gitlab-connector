# Setup guide — GitLab custom connector

End-to-end steps to stand up this connector, from registering the GitLab OAuth
app through creating a connection and testing it with a simple flow.

Prerequisites:

- Python 3.6+ (for the `paconn` CLI).
- A Power Platform environment where you can create custom connectors.
- A GitLab account with the **Maintainer** role (or higher) on at least one
  project (required for the webhook triggers).

## Contents

1. [Register a GitLab OAuth application](#1-register-a-gitlab-oauth-application)
2. [Install paconn](#2-install-paconn)
3. [Set your client ID in the connector](#3-set-your-client-id-in-the-connector)
4. [Log in to Power Platform](#4-log-in-to-power-platform)
5. [Create the custom connector](#5-create-the-custom-connector)
6. [Update the redirect URI in GitLab](#6-update-the-redirect-uri-in-gitlab)
7. [Create a connection](#7-create-a-connection)
8. [Test with a simple flow](#8-test-with-a-simple-flow)
9. [Updating the connector later](#updating-the-connector-later)
10. [Troubleshooting](#troubleshooting)

---

## 1. Register a GitLab OAuth application

You need an OAuth application in GitLab to get a **client ID** and **client secret**.

1. In GitLab, go to your application settings:
   - **GitLab.com (personal):** avatar → **Edit profile** → **Applications**.
   - **Group-owned:** your **Group** → **Settings** → **Applications**.
   - **Self-managed:** the equivalent **Applications** page on your instance.
2. Fill in:
   - **Name:** `Power Platform GitLab Connector` (anything meaningful).
   - **Redirect URI:** `https://global.consent.azure-apim.net/redirect`
     (a temporary placeholder — you'll replace it with the exact per-connector
     URL in [step 6](#6-update-the-redirect-uri-in-gitlab)).
   - **Confidential:** ✅ checked (required — Power Platform's generic OAuth2
     does not use PKCE).
   - **Scopes:** ✅ `api` (full read/write). Use `read_api` instead if you only
     need the read operations.
3. Click **Save application**.
4. Copy the **Application ID** (this is your **client ID**) and the **Secret**
   (your **client secret**). The secret is shown only once — store it securely.

---

## 2. Install paconn

`paconn` is Microsoft's CLI for custom connectors.

```powershell
pip install paconn
```

Verify:

```powershell
paconn --version
```

---

## 3. Set your client ID in the connector

Open `apiProperties.json` and set the `clientId` to your GitLab **Application ID**:

```json
"clientId": "<your GitLab Application ID>",
```

The **client secret** is *not* stored in the file — you pass it on the command
line when creating/updating the connector.

> Self-managed GitLab only: also change the three OAuth URLs in
> `apiProperties.json` (`authorizationUrlTemplate`, `tokenUrlTemplate`,
> `refreshUrlTemplate`) from `https://gitlab.com/...` to your instance host.

---

## 4. Log in to Power Platform

```powershell
paconn login
```

This opens a browser to authenticate. After signing in, select the target
**environment** when prompted (this is where the connector will be created).

---

## 5. Create the custom connector

From the repository root:

```powershell
cd C:\src\gitlab-connector
paconn create -s settings.json --secret <your GitLab client secret>
```

- Using `-s settings.json` is important: it references all four files
  (`apiDefinition.swagger.json`, `apiProperties.json`, `icon.png`, and
  `script.csx`). The custom code (`script.csx`) is required because the
  connector declares `scriptOperations`.
- On success, `paconn` prints a **connector ID** and writes it (and the
  environment) back into `settings.json`, so later updates just need
  `paconn update -s settings.json`.

> If you see `Script definition url '' must be a valid URI when script
> operations are specified`, you ran `create` with individual `--api-def/...`
> flags instead of `-s settings.json`, so the script wasn't included. Re-run
> with `-s settings.json`.

---

## 6. Update the redirect URI in GitLab

Power Platform assigns each connector its **own** redirect URL, which differs
from the placeholder you registered in step 1.

1. Open the maker portal ([make.powerautomate.com](https://make.powerautomate.com)),
   switch to the environment from step 4, then go to **More** → **Discover all** →
   **Custom connectors**.
2. Open your **GitLab** connector → **Edit** (pencil) → **Security** tab.
3. Copy the **Redirect URL** shown there. It looks like:
   ```
   https://global.consent.azure-apim.net/redirect/gitlab-<suffix>
   ```
4. Back in GitLab, open your OAuth application (the one whose Application ID is in
   `apiProperties.json`) → **Edit**, and set **Redirect URI** to that exact value
   — character for character, no trailing slash or spaces.
5. **Save application** in GitLab.

> "The redirect URI included is not valid" during sign-in means this value
> doesn't match. Recreating the connector changes the suffix, so re-copy it if
> you ever recreate.

---

## 7. Create a connection

1. On the connector, go to the **Test** tab (or **Connections**) → **+ New connection**.
2. Enter the **GitLab host** without a scheme, e.g. `gitlab.com` (or your
   self-managed host such as `gitlab.mycompany.com`).
3. You'll be redirected to GitLab → **Authorize** the application.
4. On success the connection is created and ready to use.

Quick check: on the **Test** tab, run **Get current user** — a `200` response
with your GitLab profile confirms OAuth and host routing both work.

---

## 8. Test with a simple flow

A minimal flow to confirm the issue webhook trigger works end to end.

1. In [make.powerautomate.com](https://make.powerautomate.com) (correct
   environment), create a new **Automated cloud flow** (you can skip the built-in
   trigger picker and search for the connector).
2. Choose the **GitLab** trigger **When an issue is created or updated**.
3. Select your connection and enter a **Project ID or path** — either the numeric
   ID or a path like `my-group/my-project`.
4. Add a simple action so the run does something visible, for example:
   - **Notifications → Send me an email notification**, or
   - **Office 365 Outlook → Send an email (V2)**, or
   - a **Compose** action that outputs `Issue title` from the trigger.
   Use dynamic content from the trigger (e.g. `object_attributes Title`,
   `object_attributes URL`) in the message.
5. **Save** the flow. Saving registers the GitLab webhook (turning the flow on).
6. In GitLab, **create or edit an issue** in that project.
7. Open the flow's **run history** — you should see a successful run, and your
   email/compose output should contain the issue details.

Verify the webhook lifecycle:

- While the flow is **on**, the hook is visible in GitLab under
  **Project → Settings → Webhooks** (or via the connector's **List project hooks**
  action).
- Turn the flow **off** — the hook is removed automatically (auto-unsubscribe).

---

## Updating the connector later

After editing any of the connector files, push changes with:

```powershell
cd C:\src\gitlab-connector
paconn update -s settings.json --secret <your GitLab client secret>
```

`settings.json` already holds the connector ID and environment from the create
step, so no other arguments are needed. Re-pass `--secret` so the client secret
stays set.

> Definition changes can take a while to appear in the designer (portal caching).
> `script.csx` changes are runtime and take effect on the next subscribe.

---

## Troubleshooting

| Symptom | Likely cause / fix |
| --- | --- |
| Fails before any GitLab screen; "Failed to login. Invalid login for user." | OAuth URLs unset — ensure `authorizationUrlTemplate`/`tokenUrlTemplate`/`refreshUrlTemplate` keys are used (generic OAuth2), and the app is **Confidential**. |
| "The redirect URI included is not valid" (on GitLab's screen) | The connector's per-connector redirect URL isn't registered in GitLab. Copy it from the Security tab into the GitLab app exactly (step 6). |
| "requires the property 'body' to be of type 'Object' but is of type 'String'" | Base path missing — `/api/v4` must be in the `dynamichosturl` template, not `basePath`. |
| `Script definition url '' must be a valid URI...` on create | Use `paconn create -s settings.json ...` so `script.csx` is included. |
| "Flow save failed ... must be of type 'OpenApiConnectionWebhook'" | Stale cached definition. Wait for propagation, then create a **fresh** flow and re-add the trigger. |
| Webhook stays in GitLab after turning the flow off | Old connector build. Update to the latest `script.csx` (builds `Location` from the connector-facing URI); delete the orphaned hook manually once. |

For deeper background on any of these, see
[IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md).
