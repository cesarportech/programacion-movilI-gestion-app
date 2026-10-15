// Custom FREDI authentication: PBKDF2 local verifiers, short-lived opaque cloud tokens.
// verify_jwt=false is intentional: authorization is performed inside the gateway on EVERY request.
// SUPABASE_SERVICE_ROLE_KEY stays only in the Edge runtime, never in the desktop app.
const url = Deno.env.get("SUPABASE_URL")!;
const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const digest = async (text: string) => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))), b => b.toString(16).padStart(2, "0")).join("");
const from64 = (v: string) => Uint8Array.from(atob(v), x => x.charCodeAt(0));
async function rpc(op: string, args: unknown) {
  const response = await fetch(`${url}/rest/v1/rpc/fredi_gateway`, {
    method: "POST", headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
    body: JSON.stringify({ op, args }),
  });
  if (!response.ok) throw new Error("gateway_failed");
  return await response.json();
}
function reply(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store" } });
}
function result(data: { error?: string }) {
  const codes: Record<string, number> = { unauthorized: 401, forbidden: 403, rate_limit: 429, conflict: 409, not_found: 404 };
  return reply(data, data.error ? codes[data.error] ?? 422 : 200);
}
Deno.serve(async (req: Request) => {
  try {
    const route = new URL(req.url).pathname.split("/fredi-api/")[1];
    const company = /^v1\/contabilidades\/([1-9][0-9]*)$/.exec(route);
    const read = !!company || route === "v1/contabilidades" || route === "v1/catalogos";
    if (!read && !["v1/auth/login", "v1/auth/logout", "v1/sync", "v1/status"].includes(route)) return reply({ error: "not_found" }, 404);
    if (req.method !== (read ? "GET" : "POST")) return reply({ error: "method_not_allowed" }, 405);
    // Reject oversized streams before JSON parsing. Never log credentials or accounting payloads.
    const reader = req.body?.getReader(); const chunks: Uint8Array[] = []; let size = 0;
    if (reader) while (true) {
      const { value, done } = await reader.read(); if (done) break;
      size += value.length; if (size > 10 * 1024 * 1024) { await reader.cancel(); return reply({ error: "payload_too_large" }, 413); }
      chunks.push(value);
    }
    const bytes = new Uint8Array(size); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    const body = size ? JSON.parse(new TextDecoder().decode(bytes)) : {};
    if (route === "v1/auth/login") {
      if (typeof body.dataset !== "string" || !/^[0-9a-f-]{36}$/i.test(body.dataset) ||
          typeof body.username !== "string" || body.username.length > 60 || typeof body.password !== "string" || body.password.length > 100)
        return reply({ error: "unauthorized" }, 401);
      const lookup = await rpc("login_lookup", { dataset: body.dataset, username: body.username.trim().toLowerCase(),
        ip: await digest(req.headers.get("x-forwarded-for")?.split(",")[0] ?? "unknown") });
      if (lookup.error) return result(lookup);
      const user = lookup.user;
      const material = await crypto.subtle.importKey("raw", new TextEncoder().encode(body.password), "PBKDF2", false, ["deriveBits"]);
      const computed = new Uint8Array(await crypto.subtle.deriveBits({ name: "PBKDF2", salt: from64(user.Salt), iterations: 210000, hash: "SHA-256" }, material, 256));
      const expected = from64(user.PasswordHash); let difference = computed.length ^ expected.length;
      for (let i = 0; i < computed.length; i++) difference |= computed[i] ^ (expected[i] ?? 0);
      if (difference !== 0) return reply({ error: "unauthorized" }, 401);
      const token = Array.from(crypto.getRandomValues(new Uint8Array(32)), b => b.toString(16).padStart(2, "0")).join("");
      const finished = await rpc("login_finish", { dataset: body.dataset, userId: user.Id, version: user.SecurityVersion, tokenHash: await digest(token) });
      if (finished.error) return result(finished);
      return reply({ token });
    }
    const auth = req.headers.get("authorization") ?? "";
    if (!/^Bearer [0-9a-f]{64}$/.test(auth)) return reply({ error: "unauthorized" }, 401);
    const op = company ? "company" : route === "v1/contabilidades" ? "companies" : route === "v1/catalogos" ? "catalogs" : route === "v1/sync" ? "sync" : route === "v1/status" ? "status" : "logout";
    return result(await rpc(op, { tokenHash: await digest(auth.slice(7)), body, companyId: company ? Number(company[1]) : undefined }));
  } catch {
    return reply({ error: "request_failed" }, 422);
  }
});
