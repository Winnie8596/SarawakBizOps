import type { WorkOrder } from '../../types'
import { useReportPdf } from '../../hooks/useReportPdf'

// The last step of the technician's flow (PRD §6.8: "… Complete → View Report"): once a Manager has generated the
// job's report, the technician can open it on the phone. Until then, say who makes it.
export function JobReportButton({ job }: { job: WorkOrder }) {
  const report = job.report ?? null
  const pdf = useReportPdf(job.id, report?.reportNumber)

  return (
    <>
      <h3 className="panel-subtitle">Service report</h3>
      {report ? (
        <>
          <button className="btn btn-ghost tech-secondary-btn" onClick={pdf.open} disabled={pdf.fetching}>
            {pdf.fetching ? 'Opening…' : `View report ${report.reportNumber}`}
          </button>
          {pdf.error && <p className="form-error">{pdf.error}</p>}
        </>
      ) : (
        <p className="form-hint">The Manager generates the service report after checking the completed job.</p>
      )}
    </>
  )
}
