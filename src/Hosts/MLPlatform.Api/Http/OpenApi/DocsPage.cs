namespace MLPlatform.Api.Http.OpenApi;

/// <summary>Swagger UI over the embedded OpenAPI documents; full API only when signed in.</summary>
public static class DocsPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>MLPlatform API</title>
          <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.17.14/swagger-ui.css">
          <style>body{margin:0;background:#fff}.swagger-ui .topbar{display:none}</style>
        </head>
        <body>
          <div id="docs"></div>
          <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.17.14/swagger-ui-bundle.js"></script>
          <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.17.14/swagger-ui-standalone-preset.js"></script>
          <script>
            (async () => {
              const gateway = { url: "/openapi/gateway.json", name: "Gateway" };
              const full = await fetch("/openapi/v1.json", { credentials: "include" }).then(r => r.ok).catch(() => false);
              SwaggerUIBundle({
                dom_id: "#docs",
                urls: full ? [{ url: "/openapi/v1.json", name: "MLPlatform" }, gateway] : [gateway],
                presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
                layout: "StandaloneLayout",
                deepLinking: true,
                validatorUrl: null,
                persistAuthorization: true,
                withCredentials: true,
                requestInterceptor: req => { req.credentials = "include"; return req; },
              });
            })();
          </script>
        </body>
        </html>
        """;
}
