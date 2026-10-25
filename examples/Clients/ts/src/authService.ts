import { createHmac } from "node:crypto";

/**
 * Mirrors examples/Shared/Services/AuthService.cs (demo JWT minting only).
 */
export const Roles = {
  User: "User",
  Administrator: "Administrator",
} as const;

const Issuer: string = "Auth";
const Audience: string = "*";
const SigningKey: string = "7f3A9xK2mQ8vL5pR1tY6wN4cB9sD3hG8";

// ASP.NET ClaimTypes.Name / ClaimTypes.Role URIs
const ClaimName: string = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
const ClaimRole: string = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function base64Url(input: string | Buffer): string {
  const buf: Buffer = typeof input === "string" ? Buffer.from(input, "utf8") : input;
  return buf
    .toString("base64")
    .replace(/=/g, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
}

/**
 * HS256 JWT matching AuthService.CreateToken (HMAC-SHA256, issuer Auth, audience *).
 */
export function createToken(name: string, ...roles: string[]): string {
  const header: string = base64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));

  const claims: Record<string, string | string[] | number> = {
    [ClaimName]: name,
    iss: Issuer,
    aud: Audience,
    exp: Math.floor(Date.now() / 1000) + 60 * 60,
  };

  if (roles.length === 1) {
    claims[ClaimRole] = roles[0]!;
  } else if (roles.length > 1) {
    claims[ClaimRole] = roles;
  }

  const payload: string = base64Url(JSON.stringify(claims));
  const data: string = `${header}.${payload}`;
  const signature: string = base64Url(
    createHmac("sha256", SigningKey).update(data, "utf8").digest(),
  );

  return `${data}.${signature}`;
}
