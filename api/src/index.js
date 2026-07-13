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
// The Funnel URL is public knowledge (it is announced by the Funnel's own TLS
// certificate and was previously baked into config.js), so hardcoding it here
// adds no exposure and keeps the function free of portal-managed
// configuration. Rotating the Funnel host means editing THIS constant — see
// docs/operations.md setup step 2 for the rotation checklist.
const BACKEND = "https://paskamyrsky.tail6ed53b.ts.net";

// Fail closed: only the path prefixes the pages (and feedctl's board probe)
// actually use are relayed, so a future backend endpoint never becomes
// publicly proxied merely by shipping. A prefix covers the exact path and any
// subpath — "report" covers /report?from=… and /report/snapshot.
const ALLOWED_PREFIXES = ["health", "schema", "interpret", "report", "live/feedback", "live/report"];

app.http("proxy", {
  methods: ["GET", "POST"],
  authLevel: "anonymous",
  route: "{*path}",
  handler: async (request, context) => {
    // Path from the RAW request URL, not request.params.path: route params
    // arrive URL-decoded, and re-interpolating a decoded '#' or '/' into the
    // fetch URL would truncate or re-split the forwarded path. The pathname
    // keeps the original percent-encoding; the /api prefix is stripped if the
    // host passes it through.
    const url = new URL(request.url);
    const path = url.pathname.replace(/^\/api(?=\/|$)/, "").replace(/^\//, "");
    if (!ALLOWED_PREFIXES.some(p => path === p || path.startsWith(p + "/")))
      return { status: 404, jsonBody: { error: "not proxied" } };

    const init = { method: request.method, headers: {}, redirect: "manual" };
    const contentType = request.headers.get("content-type");
    if (contentType) init.headers["content-type"] = contentType;
    if (request.method === "POST") init.body = Buffer.from(await request.arrayBuffer());
    try {
      // No artificial timeout: a first live report legitimately runs MANY
      // sequential gated LLM calls (far past any single-call bound), so the
      // platform's own gateway limit is the effective ceiling here.
      const res = await fetch(`${BACKEND}/${path}${url.search}`, init);
      const headers = { "cache-control": "no-store" };
      const resType = res.headers.get("content-type");
      if (resType) headers["content-type"] = resType;
      return { status: res.status, headers, body: Buffer.from(await res.arrayBuffer()) };
    } catch (err) {
      // Backend down is a NORMAL state for this demo (snapshot-first design):
      // answer with a clean 502 the pages already treat as "server offline".
      context.warn(`proxy: ${path} unreachable: ${err.message}`);
      return { status: 502, jsonBody: { error: "backend unreachable" } };
    }
  },
});
