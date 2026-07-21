public class Script : ScriptBase
{
    // Bound (via "scriptOperations" in apiProperties.json) to the webhook
    // subscribe triggers only. For those operations this script:
    //   1. Renames the "notificationUrl" body property to "url" (the field
    //      GitLab's create-hook API expects). We expose it as "notificationUrl"
    //      because designer surfaces handle the callback URL inconsistently
    //      under other names.
    //   2. Synthesizes a Location response header from the created hook's
    //      project_id + id, because GitLab does not return one. Power Platform
    //      DELETEs that URL to remove the hook when the flow is turned off.
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
                var projectId = hook["project_id"] != null ? hook["project_id"].ToString() : null;

                if (!string.IsNullOrEmpty(hookId) && !string.IsNullOrEmpty(projectId))
                {
                    var authority = request.RequestUri.GetLeftPart(UriPartial.Authority);
                    var location = authority + "/api/v4/projects/" + projectId + "/hooks/" + hookId;
                    response.Headers.Remove("Location");
                    response.Headers.Location = new Uri(location, UriKind.Absolute);
                }
            }
            catch
            {
                // Non-JSON body: leave unchanged.
            }
        }

        return response;
    }
}
