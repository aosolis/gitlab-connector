public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Only post-process the webhook subscribe operation. GitLab's
        // "create project hook" response returns the hook id but no Location
        // header, so Power Platform has no URL to DELETE when the flow is
        // turned off. Here we synthesize an absolute Location header pointing
        // at the created hook so the platform can auto-unsubscribe.
        if (string.Equals(this.Context.OperationId, "OnIssueEvent", StringComparison.OrdinalIgnoreCase))
        {
            var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                try
                {
                    var hook = JObject.Parse(content);
                    var hookId = hook["id"] != null ? hook["id"].ToString() : null;

                    if (!string.IsNullOrEmpty(hookId))
                    {
                        // Request URI after policies is https://<host>/api/v4/projects/<id>/hooks
                        var basePath = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                        var location = basePath + "/" + hookId;

                        response.Headers.Remove("Location");
                        response.Headers.Location = new Uri(location, UriKind.Absolute);
                    }
                }
                catch
                {
                    // If the body is not the expected JSON, return it untouched.
                }
            }

            return response;
        }

        // All other operations pass through unchanged.
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }
}
