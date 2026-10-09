import type { WorkOrder } from '../types'
import { useReportPdf } from '../hooks/useReportPdf'
import { formatDateTime, formatFileSize } from '../utils/format'

interface ServiceReportPanelProps {
  workOrder: WorkOrder
  /** A Manager: may generate the report (the API enforces it too). */
  canGenerate: boolean
  generating: boolean
  /** Another action on the page is running. */
  busy: boolean
  onGenerate: () => void
}

/**
 * The job's service report on the office work-order page (Phase 10): its number and revision, Open/Download, and
 * for a Manager Generate — or Regenerate while the job is only Completed. Once the job is Approved the report is
 * locked (BR-14); a job approved before it had a report may still get its one.
 */
export function ServiceReportPanel({ workOrder, canGenerate, generating, busy, onGenerate }: ServiceReportPanelProps) {
  const report = workOrder.report ?? null
  const pdf = useReportPdf(workOrder.id, report?.reportNumber)
  const generatable = workOrder.status === 'Completed' || (workOrder.status === 'Approved' && !report)

  return (
    <div className="panel">
      <h2 className="panel-title">Service report</h2>

      {report ? (
        <dl className="detail-list">
          <div>
            <dt>Report no.</dt>
            <dd className="mono">{report.reportNumber}</dd>
          </div>
          <div>
            <dt>Revision</dt>
            <dd>{report.revision}</dd>
          </div>
          <div>
            <dt>Generated</dt>
            <dd>
              {formatDateTime(report.generatedAt)} by {report.generatedByName}
            </dd>
          </div>
          <div>
            <dt>Size</dt>
            <dd>{formatFileSize(report.sizeBytes)}</dd>
          </div>
        </dl>
      ) : (
        <p className="form-hint">
          {canGenerate
            ? 'No report yet. Generate it once you have checked the completed job.'
            : 'No report yet. A Manager generates it once the job is completed.'}
        </p>
      )}

      {report?.isLocked && (
        <p className="form-hint">Locked — the job is approved, so this report can no longer be regenerated.</p>
      )}
      {report && !report.isLocked && canGenerate && (
        <p className="form-hint">
          Regenerating keeps the report number and adds a revision. Approving the job locks it.
        </p>
      )}
      {pdf.error && <p className="form-error">{pdf.error}</p>}

      <div className="panel-actions">
        {report && (
          <>
            <button className="btn btn-ghost" onClick={pdf.open} disabled={pdf.fetching}>
              Open PDF
            </button>
            <button className="btn btn-ghost" onClick={pdf.download} disabled={pdf.fetching}>
              {pdf.fetching ? 'Loading…' : 'Download PDF'}
            </button>
          </>
        )}
        {canGenerate && generatable && (
          <button className="btn btn-primary" onClick={onGenerate} disabled={busy}>
            {generating ? 'Generating…' : report ? 'Regenerate report' : 'Generate report'}
          </button>
        )}
      </div>
    </div>
  )
}
