import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'
import { getReportPdf } from '../api/reports'
import { downloadBlob, showBlobInTab } from '../utils/saveBlob'

/**
 * Opens or downloads a job's service report. The PDF is fetched with the bearer token (it has no public URL)
 * and handed to the browser as a Blob, saved as "<report number>.pdf".
 */
export function useReportPdf(workOrderId: number, reportNumber: string | undefined) {
  const { auth } = useAuth()
  const [fetching, setFetching] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function fetchPdf(inNewTab: boolean) {
    if (!auth || !reportNumber) return
    // The tab must open during the click itself; it is pointed at the PDF once it has arrived.
    const tab = inNewTab ? window.open('', '_blank') : null
    setFetching(true)
    setError(null)
    try {
      const blob = await getReportPdf(auth.token, workOrderId)
      const fileName = `${reportNumber}.pdf`
      if (inNewTab) showBlobInTab(tab, blob, fileName)
      else downloadBlob(blob, fileName)
    } catch (err) {
      tab?.close()
      setError(err instanceof ApiError ? err.message : 'Could not load the report.')
    } finally {
      setFetching(false)
    }
  }

  return {
    fetching,
    error,
    open: () => fetchPdf(true),
    download: () => fetchPdf(false)
  }
}
