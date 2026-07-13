const { app } = require("@azure/functions");

// Same-origin backend proxy (ADR-0025). The pages call /api/* on their own
// origin; this function forwards server-side to the Tailscale Funnel. The
// browser therefore never makes a cross-origin or public-to-local request —
// which matters because Chrome's Local Network Access permission (2026)
// blocks a public page from fetching a host that resolves to a private
// address, exactly what the Funnel hostname does on the operator's own
// machine (MagicDNS). Mirrors the mikkonumminen.dev same-origin proxy
// decision (that repo's ADR-0012).
//
// The Funnel URL is public knowledge (it was previously baked into config.js
// and sits in the API's CORS allowlist), so hardcoding it here adds no
// exposure and keeps the function free of portal-managed configuration.
const BACKEND = "https://paskamyrsky.tail6ed53b.ts.net";

app.http("proxy", {
  methods: ["GET", "POST"],
  authLevel: "anonymous",
  route: "{*path}",
  handler: async (request, context) => {
    const search = new URL(request.url).search;
    const target = `${BACKEND}/${request.params.path ?? ""}${search}`;
    const init = { method: request.method, headers: {}, redirect: "manual" };
    const contentType = request.headers.get("content-type");
    if (contentType) init.headers["content-type"] = contentType;
    if (request.method === "POST") init.body = Buffer.from(await request.arrayBuffer());
    try {
      const res = await fetch(target, init);
      const headers = { "cache-control": "no-store" };
      const resType = res.headers.get("content-type");
      if (resType) headers["content-type"] = resType;
      return { status: res.status, headers, body: Buffer.from(await res.arrayBuffer()) };
    } catch (err) {
      // Backend down is a NORMAL state for this demo (snapshot-first design):
      // answer with a clean 502 the pages already treat as "server offline".
      context.warn(`proxy: ${target} unreachable: ${err.message}`);
      return { status: 502, jsonBody: { error: "backend unreachable" } };
    }
  },
});
