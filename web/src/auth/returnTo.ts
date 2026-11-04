/**
 * The page to open after sign-in, e.g. an invitation link opened without a session. Kept in localStorage because
 * registration continues in another tab (the email confirmation link). Only in-app paths are accepted; an
 * invitation token is safe to keep here because accepting it also needs the invited email's account.
 */
const key = 'cesio.returnTo'

function isInApp(path: string | null): path is string {
  return path !== null && path.startsWith('/') && !path.startsWith('//')
}

export function rememberReturnTo(path: string) {
  if (!isInApp(path)) {
    return
  }
  try {
    localStorage.setItem(key, path)
  } catch {
    // Without storage the user opens the link again after signing in.
  }
}

export function pendingReturnTo(): string | null {
  try {
    const path = localStorage.getItem(key)
    return isInApp(path) ? path : null
  } catch {
    return null
  }
}

/** The remembered page (forgotten once read), or the home page. */
export function takeReturnTo(): string {
  const path = pendingReturnTo()
  try {
    localStorage.removeItem(key)
  } catch {
    // Nothing was stored.
  }
  return path ?? '/'
}
