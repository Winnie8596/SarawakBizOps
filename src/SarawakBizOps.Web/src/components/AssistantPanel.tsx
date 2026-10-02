import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getTechnicianAssistance } from '../api/ai'
import { ApiError } from '../api/client'
import type { AssistantLikelihood, TechnicianAssistance } from '../types'
import { formatDateTime, formatQuantity } from '../utils/format'

const LIKELIHOOD_TONE: Record<AssistantLikelihood, string> = {
  High: 'tone-amber',
  Medium: 'tone-steel',
  Low: 'tone-grey'
}

interface AssistantPanelProps {
  workOrderId: number
  /** Technician: puts the suggestions into the job's notes field, where they can be edited — they are only saved
   *  with the job, never by the assistant. Without it (office) the panel offers a copy to the clipboard instead. */
  onCopyToNotes?: (text: string) => void
  /** Office: earlier jobs link to their work order (a Technician may not be able to open them). */
  linkPastCases?: boolean
}

/**
 * The AI technician assistant (Phase 13, AI-01): asks for suggestions about one job and shows them, always under
 * the server's disclaimer. Advice only — nothing here changes the job; the technician decides what to copy.
 */
export function AssistantPanel({ workOrderId, onCopyToNotes, linkPastCases = false }: AssistantPanelProps) {
  const { auth } = useAuth()
  const [observations, setObservations] = useState('')
  const [answer, setAnswer] = useState<TechnicianAssistance | null>(null)
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const canCopyToClipboard = !onCopyToNotes && typeof navigator !== 'undefined' && !!navigator.clipboard

  async function handleAsk() {
    if (!auth) return
    setAsking(true)
    setError(null)
    setNotice(null)
    try {
      setAnswer(await getTechnicianAssistance(auth.token, workOrderId, observations.trim()))
    } catch (err) {
      // 503 (provider down, timed out or answered nonsense) and 429 (asked too often) carry their own message.
      setError(err instanceof ApiError ? err.message : 'The AI assistant could not be reached. Try again later.')
    } finally {
      setAsking(false)
    }
  }

  async function handleCopy() {
    if (!answer) return
    const text = assistanceAsText(answer)
    if (onCopyToNotes) {
      onCopyToNotes(text)
      setNotice('Added to the notes below. Check and edit them — they are saved only with the job.')
      return
    }
    try {
      await navigator.clipboard.writeText(text)
      setNotice('Copied to the clipboard.')
    } catch {
      setNotice('Could not copy. Select the text instead.')
    }
  }

  return (
    <section className="panel assistant-panel" aria-labelledby={`assistant-${workOrderId}`}>
      <h2 className="panel-title" id={`assistant-${workOrderId}`}>AI assistant</h2>

      <div className="field">
        <label className="field-label" htmlFor={`assistant-observations-${workOrderId}`}>
          What have you noticed on site? (optional)
        </label>
        <textarea
          id={`assistant-observations-${workOrderId}`}
          value={observations}
          onChange={e => setObservations(e.target.value)}
          rows={2}
          maxLength={1000}
        />
      </div>

      <div className="assistant-actions">
        <button type="button" className="btn btn-ghost" onClick={() => void handleAsk()} disabled={asking}>
          {asking ? 'Thinking…' : answer ? 'Ask again' : 'Get suggestions'}
        </button>
      </div>

      {error && <p className="form-error" role="alert">{error}</p>}

      {answer && (
        <div className="assistant-answer">
          <p className="assistant-disclaimer" role="note">{answer.disclaimer}</p>

          <h3 className="panel-subtitle">Possible causes</h3>
          <ul className="plain-list">
            {answer.possibleCauses.map((c, i) => (
              <li key={i}>
                <span className={`status-badge ${LIKELIHOOD_TONE[c.likelihood]}`}>{c.likelihood}</span>{' '}
                <strong>{c.cause}</strong>
                {c.reasoning && <span className="muted"> — {c.reasoning}</span>}
              </li>
            ))}
          </ul>

          <h3 className="panel-subtitle">Suggested checks</h3>
          <ol className="assistant-checks">
            {answer.suggestedChecks.map((check, i) => <li key={i}>{check}</li>)}
          </ol>

          <h3 className="panel-subtitle">Earlier jobs on this equipment</h3>
          {answer.relevantPastCases.length === 0 ? (
            <p className="form-hint">
              {answer.pastCasesConsidered === 0
                ? 'No earlier completed jobs on this equipment.'
                : 'None of the earlier jobs on this equipment look related.'}
            </p>
          ) : (
            <ul className="plain-list">
              {answer.relevantPastCases.map(c => (
                <li key={c.workOrderId}>
                  {linkPastCases ? <Link to={`/work-orders/${c.workOrderId}`}>#{c.workOrderId}</Link> : `#${c.workOrderId}`}
                  {' · '}{formatDateTime(c.completedAt)}
                  <div>{c.problemDescription}</div>
                  <div className="muted">Found: {c.diagnosis}</div>
                  {c.whyRelevant && <div className="muted">Why: {c.whyRelevant}</div>}
                </li>
              ))}
            </ul>
          )}

          <h3 className="panel-subtitle">Parts that may be needed</h3>
          {answer.relevantParts.length === 0 ? (
            <p className="form-hint">No particular parts suggested.</p>
          ) : (
            <ul className="plain-list">
              {answer.relevantParts.map(p => (
                <li key={p.partId}>
                  <span className="mono">{p.partNumber}</span> {p.name}
                  <span className="muted"> · {formatQuantity(p.quantityOnHand)} {p.unit} in stock</span>
                  {p.whyRelevant && <div className="muted">{p.whyRelevant}</div>}
                </li>
              ))}
            </ul>
          )}

          <p className="form-hint">
            {answer.generatedBy} · {formatDateTime(answer.generatedAt)}
          </p>

          {(onCopyToNotes || canCopyToClipboard) && (
            <div className="assistant-actions">
              <button type="button" className="btn btn-ghost" onClick={() => void handleCopy()}>
                {onCopyToNotes ? 'Copy to notes' : 'Copy suggestions'}
              </button>
            </div>
          )}
          {notice && <p className="form-hint" role="status">{notice}</p>}
        </div>
      )}
    </section>
  )
}

/** The suggestions as plain text for the job's notes, labelled so no one mistakes them for the technician's own findings. */
export function assistanceAsText(answer: TechnicianAssistance): string {
  const lines = ['AI suggestions (not a certified diagnosis):']
  lines.push('Possible causes: ' + answer.possibleCauses.map(c => `${c.cause} (${c.likelihood})`).join('; '))
  lines.push('Checks: ' + answer.suggestedChecks.map((check, i) => `${i + 1}. ${check}`).join(' '))
  if (answer.relevantPastCases.length > 0) {
    lines.push('Earlier jobs: ' + answer.relevantPastCases.map(c => `#${c.workOrderId}`).join(', '))
  }
  if (answer.relevantParts.length > 0) {
    lines.push('Parts: ' + answer.relevantParts.map(p => p.partNumber).join(', '))
  }
  return lines.join('\n')
}
