import { NextResponse, type NextRequest } from "next/server";

/** Must match AuthCookie:Name in the backend configuration. */
const SESSION_COOKIE = "growdesk_marist_session";

const PUBLIC_PATHS = ["/login"];

/**
 * First line of route protection: without a session cookie there is no point rendering an app
 * page, so redirect to /login straight away. The cookie's validity is still checked by the API
 * on every request; an expired cookie is caught client-side by the auth guard.
 */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const hasSession = request.cookies.has(SESSION_COOKIE);

  if (pathname === "/") {
    return NextResponse.redirect(new URL(hasSession ? "/dashboard" : "/login", request.url));
  }

  if (!hasSession && !PUBLIC_PATHS.some((p) => pathname.startsWith(p))) {
    const login = new URL("/login", request.url);
    login.searchParams.set("next", pathname + search);
    return NextResponse.redirect(login);
  }

  return NextResponse.next();
}

export const config = {
  // Skip the API proxy, Next.js internals and static files.
  // Also public: the capture extension download and Chrome's update files (/downloads/*, /capture/*).
  // "capture/" needs the slash, so the /capture-guide page still requires signing in.
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|robots.txt|downloads/|capture/|.*\\.(?:svg|png|jpg|jpeg|webp|ico|zip|crx)$).*)"],
};
