import { useEffect, useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { askManagerInsights, getManagerInsightTopics } from '../api/ai'
import { ApiError } from '../api/client'
import type { ManagerInsight, ManagerInsightFigure, ManagerInsightTopic } from '../types'
import { formatDateTime, formatMoney, formatPeriod, formatQuantity } from '../utils/format'

const MAX_QUESTION = 300

/**
 * AI Manager Insights (Phase 14, AI-02, AC-10). The server maps the question to one of the dashboard's analytics
 * operations by rules and asks the model only to word the result, so the page always shows the figures themselves
 * beside the summary — exactly what the model was given — and shows them even when there is no summary.
 */
export function ManagerInsightsPage() {
  const { auth } = useAuth()
  const [topics, setTopics] = useState<ManagerInsightTopic[]>([])
  const [topicsError, setTopicsError] = useState<string | null>(null)
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState<ManagerInsight | null>(null)
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false
    getManagerInsightTopics()
      .then(list => {
        if (!cancelled) setTopics(list)
      })
      .catch(() => {
        if (!cancelled) setTopicsError('The example questions could not be loaded; you can still ask.')
      })
    return () => {
      cancelled = true
    }
  }, [auth])

  async function ask(text: string) {
    if (!auth || !text.trim()) return
    setQuestion(text)
    setAsking(true)
    setError(null)
    try {
      setAnswer(await askManagerInsights(text.trim()))
    } catch (err) {
      // 429 (asked too often) and validation errors carry their own message.
      setError(err instanceof ApiError ? err.message : 'The AI insights could not be reached. Try again later.')
    } finally {
      setAsking(false)
    }
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    void ask(question)
  }

  return (
    <div>
      <h1 className="page-title">AI Insights</h1>
      <p className="page-subtitle">
        Ask about the business in plain words. Every answer comes from the same analytics as the dashboard; the AI only
        puts the figures into words, and the figures are always shown beside it. Times are Malaysia time (MYT).
      </p>

      <section className="panel" aria-labelledby="insight-ask">
        <h2 className="panel-title" id="insight-ask">
          Ask a question
        </h2>
        <form className="insight-form" onSubmit={handleSubmit}>
          <div className="field">
            <label className="field-label" htmlFor="insight-question">
              Your question
            </label>
            <input
              id="insight-question"
              value={question}
              onChange={e => setQuestion(e.target.value)}
              maxLength={MAX_QUESTION}
              placeholder="e.g. How many jobs were completed last month?"
              autoComplete="off"
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={asking || !question.trim()}>
            {asking ? 'Asking…' : 'Ask'}
          </button>
        </form>

        <h3 className="panel-subtitle">Try asking</h3>
        {topicsError ? (
          <p className="form-hint">{topicsError}</p>
        ) : topics.length === 0 ? (
          <p className="form-hint">Loading example questions…</p>
        ) : (
          <ExampleQuestions topics={topics} disabled={asking} onAsk={q => void ask(q)} />
        )}
      </section>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      {answer?.outcome === 'Refused' && (
        <section className="panel insight-refusal" aria-labelledby="insight-refused">
          <h2 className="panel-title" id="insight-refused">
            I can't answer that
          </h2>
          <p role="status">{answer.refusalReason}</p>
          <h3 className="panel-subtitle">What you can ask about</h3>
          <ul className="plain-list">
            {answer.supportedTopics.map(t => (
              <li key={t.id}>
                <strong>{t.title}</strong> <span className="muted">— {t.description}</span>
                <div>
                  <button
                    type="button"
                    className="btn btn-link"
                    disabled={asking}
                    onClick={() => void ask(t.exampleQuestion)}
                  >
                    {t.exampleQuestion}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {answer?.outcome === 'Answered' && <Answer answer={answer} />}
    </div>
  )
}

function ExampleQuestions({
  topics,
  disabled,
  onAsk
}: {
  topics: ManagerInsightTopic[]
  disabled: boolean
  onAsk: (question: string) => void
}) {
  return (
    <ul className="insight-examples">
      {topics.map(t => (
        <li key={t.id}>
          <button type="button" className="btn btn-ghost" disabled={disabled} onClick={() => onAsk(t.exampleQuestion)}>
            {t.exampleQuestion}
          </button>
        </li>
      ))}
    </ul>
  )
}

function Answer({ answer }: { answer: ManagerInsight }) {
  const period = answer.period ? formatPeriod(answer.period.fromMonth, answer.period.toMonth) : null

  return (
    <div className="insight-answer">
      <section className="panel" aria-labelledby="insight-summary">
        <h2 className="panel-title" id="insight-summary">
          {answer.topic?.title}
        </h2>
        <p className="muted">You asked: “{answer.question}”</p>
        {answer.disclaimer && (
          <p className="assistant-disclaimer" role="note">
            {answer.disclaimer}
          </p>
        )}
        {answer.summary ? (
          <p className="insight-summary">{answer.summary}</p>
        ) : (
          <p className="form-hint" role="status">
            {answer.summaryUnavailableReason}
          </p>
        )}
        {answer.note && <p className="form-hint">{answer.note}</p>}
        <p className="form-hint">
          {answer.generatedBy && <>{answer.generatedBy} · </>}
          {formatDateTime(answer.generatedAt)}
        </p>
      </section>

      <section className="panel" aria-labelledby="insight-figures">
        <h2 className="panel-title" id="insight-figures">
          The figures{' '}
          <span className="muted">
            {answer.periodLabel === 'Now' ? 'as of now' : period ? `· ${period}` : `· ${answer.periodLabel}`}
          </span>
        </h2>
        <div className="table-scroll">
          <table className="data-table">
            <caption className="visually-hidden">Figures computed by the analytics</caption>
            <thead>
              <tr>
                <th scope="col">Figure</th>
                <th scope="col" className="num">
                  Value
                </th>
              </tr>
            </thead>
            <tbody>
              {answer.figures.map((f, i) => (
                <tr key={i}>
                  <td>{f.label}</td>
                  <td className="num">{formatFigure(f)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {answer.definition && <p className="form-hint">How it is counted: {answer.definition}</p>}
      </section>
    </div>
  )
}

/** The value exactly as computed (no rounding beyond the server's), so it can be checked against the summary. */
export function formatFigure(figure: ManagerInsightFigure): string {
  if (figure.value === null) return 'no data'
  if (figure.unit === 'RM') return formatMoney(figure.value)
  // "1 job", not "1 jobs": the count units are plural words.
  const unit = figure.value === 1 && COUNT_UNITS.includes(figure.unit) ? figure.unit.slice(0, -1) : figure.unit
  return `${formatQuantity(figure.value)} ${unit}`
}

const COUNT_UNITS = ['jobs', 'requests', 'parts', 'hours']
