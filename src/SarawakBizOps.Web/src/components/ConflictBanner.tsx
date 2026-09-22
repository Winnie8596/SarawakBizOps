// The one place every page renders "record changed — refresh" and every other action error, so a
// 409 conflict always looks the same regardless of which page or action triggered it.
export function ConflictBanner({ message }: { message: string | null }) {
  if (!message) return null
  return <p className="form-error" role="alert">{message}</p>
}
