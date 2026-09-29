// Files fetched with the bearer token (the report PDF) arrive as a Blob; these hand one to the browser.

// Long enough for the browser to start the download or load the tab; the URL is released afterwards.
const RELEASE_AFTER_MS = 60_000

/** Saves the blob under `fileName`, as a link with a `download` attribute would. */
export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), RELEASE_AFTER_MS)
}

/**
 * Shows the blob in a tab opened *before* the fetch started: browsers only allow a new tab straight from the
 * click, not after an await. Falls back to a download when the tab was blocked.
 */
export function showBlobInTab(tab: Window | null, blob: Blob, fileName: string) {
  if (!tab || tab.closed) {
    downloadBlob(blob, fileName)
    return
  }
  const url = URL.createObjectURL(blob)
  tab.location.href = url
  setTimeout(() => URL.revokeObjectURL(url), RELEASE_AFTER_MS)
}
