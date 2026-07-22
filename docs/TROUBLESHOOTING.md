# Troubleshooting — GitLab custom connector

Common problems and their fixes. For deeper background on any of these, see
[IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md); for setup steps, see
[SETUP_GUIDE.md](SETUP_GUIDE.md).

| Symptom | Likely cause / fix |
| --- | --- |
| Fails before any GitLab screen; "Failed to login. Invalid login for user." | OAuth URLs unset — ensure `authorizationUrlTemplate`/`tokenUrlTemplate`/`refreshUrlTemplate` keys are used (generic OAuth2), and the app is **Confidential**. |
| "The redirect URI included is not valid" (on GitLab's screen) | The connector's per-connector redirect URL isn't registered in GitLab. Copy it from the Security tab into the GitLab app exactly (setup guide, step 6). |
| "requires the property 'body' to be of type 'Object' but is of type 'String'" | Base path missing — `/api/v4` must be in the `dynamichosturl` template, not `basePath`. |
| `Script definition url '' must be a valid URI...` on create | Use `paconn create -s settings.json ...` so `script.csx` is included. |
| "Flow save failed ... must be of type 'OpenApiConnectionWebhook'" | Stale cached definition. Wait for propagation, then create a **fresh** flow and re-add the trigger. |
| Webhook stays in GitLab after turning the flow off | Old connector build. Update to the latest `script.csx` (builds `Location` from the connector-facing URI); delete the orphaned hook manually once. |
| Certification fails: "missing notification content extension" | `x-ms-notification-content` must be at the **path-item level** (sibling of the HTTP method), not inside the operation. |
| Certification fails: "The 'default' response should not have schema definition" | Remove the `schema` from `default` responses; keep only a `description`. |
| Certification fails: "internal ... must be required" | Internal body properties that carry a `default` must be listed in the schema's `required` array. |
| Trigger loads an old shape / changes not visible in the designer | Portal Swagger caching — can take minutes to hours. `script.csx` (runtime) changes take effect on the next subscribe. |
