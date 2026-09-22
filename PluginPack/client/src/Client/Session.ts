// The C# host hands the loader page a random per-session token (options.token).
// Every request that changes state (PUT the document, POST events / images,
// directory listings) must echo it, otherwise the host answers 403. Document
// content that ends up in the DOM never sees the token because it is only kept
// inside this bundle.

export const TOKEN_HEADER = "X-AnotherMarkdown-Token";

let sessionToken = "";

export function setSessionToken(token: string | undefined | null) {
  if (token) {
    sessionToken = token;
  }
}

export function authHeaders(extra: Record<string, string> = {}): Record<string, string> {
  return {
    ...extra,
    [TOKEN_HEADER]: sessionToken
  };
}
