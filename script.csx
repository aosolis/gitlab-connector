public class Script : ScriptBase
{
    // Bound (via "scriptOperations" in apiProperties.json) to the 13 webhook
    // subscribe triggers only. For each of those operations this script:
    //   1. Renames the "notificationUrl" body property to "url" (the field
    //      GitLab's create-hook API expects). We expose it as "notificationUrl"
    //      because designer surfaces handle the callback URL inconsistently
    //      under other names.
    //   2. Sets a Location response header so Power Platform can auto-unsubscribe.
    //      GitLab does not return a Location header, so we build one from the
    //      ORIGINAL (connector-facing) request URI, rewriting the synthetic
    //      trailing segment (e.g. ".../projects/{id}/issue_hooks") to the real
    //      deletable path ".../projects/{id}/hooks/{hookId}". Because the URL
    //      stays on the connector host, the platform's DELETE routes back
    //      through the connector, carrying the connection's OAuth token and the
    //      dynamic-host policy. (Pointing straight at gitlab.com would send an
    //      unauthenticated DELETE that GitLab rejects, orphaning the hook.)
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var request = this.Context.Request;

        // 1. Remap notificationUrl -> url on the request body.
        if (request.Content != null)
        {
            var requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                try
                {
                    var body = JObject.Parse(requestBody);
                    var notificationUrl = body["notificationUrl"];
                    if (notificationUrl != null)
                    {
                        body["url"] = notificationUrl;
                        body.Remove("notificationUrl");
                        request.Content = CreateJsonContent(body.ToString());
                    }
                }
                catch
                {
                    // Non-JSON body: leave unchanged.
                }
            }
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        // 2. Set Location from the created hook so auto-unsubscribe works.
        if (response.IsSuccessStatusCode && response.Content != null)
        {
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var hook = JObject.Parse(responseBody);
                var hookId = hook["id"] != null ? hook["id"].ToString() : null;

                if (!string.IsNullOrEmpty(hookId))
                {
                    var location = BuildHookLocation(this.Context.OriginalRequestUri, hookId);
                    if (location != null)
                    {
                        response.Headers.Remove("Location");
                        response.Headers.Location = location;
                    }
                }
            }
            catch
            {
                // Non-JSON body: leave unchanged.
            }
        }

        return response;
    }

    // Turn ".../projects/{id}/<event>_hooks[?query]" (the synthetic subscribe
    // path on the connector host) into ".../projects/{id}/hooks/{hookId}", which
    // maps to the DeleteProjectHook operation.
    private static Uri BuildHookLocation(Uri originalUri, string hookId)
    {
        var text = originalUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var lastSlash = text.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return null;
        }

        var basePath = text.Substring(0, lastSlash); // ".../projects/{id}"
        var location = basePath + "/hooks/" + hookId;
        return new Uri(location, UriKind.Absolute);
    }
}
