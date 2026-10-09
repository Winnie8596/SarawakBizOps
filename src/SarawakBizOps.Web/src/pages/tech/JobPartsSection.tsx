import { useEffect, useState, type FormEvent } from 'react'
import { useAuth } from '../../auth/AuthContext'
import { getParts } from '../../api/parts'
import { ApiError } from '../../api/client'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import type { Part, WorkOrderPart } from '../../types'
import { formatMoney, formatQuantity } from '../../utils/format'

interface JobPartsSectionProps {
  parts: WorkOrderPart[]
  partsTotal: number
  /** InProgress only: parts can be added, returned, or waited for. Otherwise the list is read-only. */
  editable: boolean
  busy: boolean
  onAdd: (partId: number, quantity: number) => Promise<boolean>
  onReturn: (line: WorkOrderPart) => void
  onWait: (partId: number, quantity: number, reason: string) => Promise<boolean>
}

// The technician's Parts step (Phase 7): what is on the job with its RM cost, and a picker built for a
// phone — search as you type, tap a part, set the quantity with large −/+ buttons. When the store is
// short, the same picker offers to pause the job until the part arrives (PendingParts).
export function JobPartsSection({ parts, partsTotal, editable, busy, onAdd, onReturn, onWait }: JobPartsSectionProps) {
  const [picking, setPicking] = useState(false)

  if (!editable && parts.length === 0) return null

  return (
    <div className="panel">
      <h2 className="panel-title">Parts used</h2>

      {parts.length === 0 ? (
        <p className="form-hint">No parts used yet.</p>
      ) : (
        <ul className="job-part-list">
          {parts.map(line => (
            <li key={line.id} className="job-part-line">
              <div className="job-part-text">
                <span className="mono">{line.partNumber}</span> {line.partName}
                <span className="job-part-cost">
                  {formatQuantity(line.quantity)} {line.unit} × {formatMoney(line.unitPrice)} ={' '}
                  <strong>{formatMoney(line.lineTotal)}</strong>
                </span>
              </div>
              {editable && (
                <button
                  type="button"
                  className="btn btn-ghost job-part-return"
                  onClick={() => onReturn(line)}
                  disabled={busy}
                  aria-label={`Return ${line.partNumber}`}
                >
                  Return
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {parts.length > 0 && (
        <p className="job-part-total">
          Parts total <strong>{formatMoney(partsTotal)}</strong>
        </p>
      )}

      {editable &&
        (picking ? (
          <PartPicker
            busy={busy}
            onClose={() => setPicking(false)}
            onAdd={async (partId, quantity) => {
              if (await onAdd(partId, quantity)) setPicking(false)
            }}
            onWait={onWait}
          />
        ) : (
          <button
            type="button"
            className="btn btn-ghost tech-secondary-btn"
            onClick={() => setPicking(true)}
            disabled={busy}
          >
            Add a part
          </button>
        ))}
    </div>
  )
}

interface PartPickerProps {
  busy: boolean
  onClose: () => void
  onAdd: (partId: number, quantity: number) => Promise<void>
  onWait: (partId: number, quantity: number, reason: string) => Promise<boolean>
}

// Enough to scroll through on a phone; a longer list means the search should be narrower.
const PICKER_RESULTS = 20

function PartPicker({ busy, onClose, onAdd, onWait }: PartPickerProps) {
  const { auth } = useAuth()
  const [matches, setMatches] = useState<Part[] | null>(null)
  const [moreCount, setMoreCount] = useState(0)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const debouncedSearch = useDebouncedValue(search)
  const [selected, setSelected] = useState<Part | null>(null)
  const [quantity, setQuantity] = useState('1')
  const [waiting, setWaiting] = useState(false)
  const [reason, setReason] = useState('')

  // The catalogue can be large, so the server searches it as the technician types and sends only the best
  // few: a phone never downloads the whole parts list.
  useEffect(() => {
    if (!auth) return
    let cancelled = false
    getParts({ search: debouncedSearch, pageSize: PICKER_RESULTS })
      .then(result => {
        if (cancelled) return
        setMatches(result.items)
        setMoreCount(result.totalCount - result.items.length)
        setLoadError(null)
      })
      .catch(err => {
        if (!cancelled) setLoadError(err instanceof ApiError ? err.message : 'Could not load parts.')
      })
    return () => {
      cancelled = true
    }
  }, [auth, debouncedSearch])

  const amount = Number(quantity)
  const validAmount = quantity.trim() !== '' && Number.isFinite(amount) && amount > 0
  const short = selected !== null && validAmount && amount > selected.quantityOnHand

  function step(delta: number) {
    const next = Math.max(1, (validAmount ? amount : 0) + delta)
    setQuantity(String(next))
  }

  function handleAdd(event: FormEvent) {
    event.preventDefault()
    if (!selected || !validAmount || short) return
    void onAdd(selected.id, amount)
  }

  async function handleWait(event: FormEvent) {
    event.preventDefault()
    if (!selected || !validAmount || reason.trim() === '') return
    await onWait(selected.id, amount, reason.trim())
  }

  if (loadError) return <p className="form-error">{loadError}</p>
  if (matches === null) return <p className="form-hint">Loading parts…</p>

  if (!selected) {
    return (
      <div className="part-picker">
        <div className="field">
          <label className="field-label" htmlFor="part-search">
            Find a part
          </label>
          <input
            id="part-search"
            type="search"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Part number or name"
            autoFocus
          />
        </div>
        {matches.length === 0 ? (
          <p className="form-hint">No parts match “{search.trim()}”.</p>
        ) : (
          <ul className="part-picker-results">
            {matches.map(p => (
              <li key={p.id}>
                <button
                  type="button"
                  className="part-picker-option"
                  onClick={() => {
                    setSelected(p)
                    setQuantity('1')
                  }}
                >
                  <span>
                    <span className="mono">{p.partNumber}</span> {p.name}
                  </span>
                  <span className="part-picker-meta">
                    {formatQuantity(p.quantityOnHand)} {p.unit} in stock · {formatMoney(p.unitCost)}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
        {moreCount > 0 && (
          <p className="form-hint">
            {moreCount} more {moreCount === 1 ? 'part matches' : 'parts match'}: type more of the number or name to
            narrow the list.
          </p>
        )}
        <button type="button" className="btn btn-link" onClick={onClose}>
          Close
        </button>
      </div>
    )
  }

  return (
    <div className="part-picker">
      <p className="part-picker-selected">
        <span className="mono">{selected.partNumber}</span> {selected.name}
        <span className="part-picker-meta">
          {formatQuantity(selected.quantityOnHand)} {selected.unit} in stock · {formatMoney(selected.unitCost)} each
        </span>
      </p>

      <form onSubmit={handleAdd}>
        <div className="field">
          <label className="field-label" htmlFor="part-quantity">
            Quantity ({selected.unit})
          </label>
          <div className="stepper">
            <button
              type="button"
              className="btn btn-ghost stepper-btn"
              onClick={() => step(-1)}
              disabled={!validAmount || amount <= 1}
              aria-label="Less"
            >
              −
            </button>
            <input
              id="part-quantity"
              type="number"
              inputMode="decimal"
              min="0"
              step="any"
              value={quantity}
              onChange={e => setQuantity(e.target.value)}
            />
            <button type="button" className="btn btn-ghost stepper-btn" onClick={() => step(1)} aria-label="More">
              +
            </button>
          </div>
          {validAmount && !short && (
            <span className="form-hint">Cost {formatMoney(Math.round(amount * selected.unitCost * 100) / 100)}</span>
          )}
          {short && (
            <span className="form-error" role="alert">
              Not enough stock: only {formatQuantity(selected.quantityOnHand)} {selected.unit} on hand.
            </span>
          )}
        </div>

        {!waiting && (
          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setSelected(null)} disabled={busy}>
              Back
            </button>
            {short ? (
              <button type="button" className="btn btn-primary" onClick={() => setWaiting(true)} disabled={busy}>
                Wait for this part…
              </button>
            ) : (
              <button type="submit" className="btn btn-primary" disabled={busy || !validAmount}>
                {busy ? 'Adding…' : 'Add to job'}
              </button>
            )}
          </div>
        )}
      </form>

      {waiting && (
        <form onSubmit={handleWait}>
          <div className="field">
            <label className="field-label" htmlFor="pending-reason">
              Why is the job waiting?
            </label>
            <textarea
              id="pending-reason"
              value={reason}
              onChange={e => setReason(e.target.value)}
              rows={2}
              maxLength={500}
              required
              autoFocus
            />
            <span className="form-hint">
              The job pauses as Pending parts and can be resumed once the store has the stock.
            </span>
          </div>
          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setWaiting(false)} disabled={busy}>
              Back
            </button>
            <button type="submit" className="btn btn-primary" disabled={busy || reason.trim() === '' || !validAmount}>
              {busy ? 'Pausing…' : 'Pause job for parts'}
            </button>
          </div>
        </form>
      )}
    </div>
  )
}
