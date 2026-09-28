import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { INVENTORY_EDIT_ROLES } from '../auth/roles'
import { createPart, getLowStockParts, getParts, updatePart } from '../api/parts'
import { recordStockMovement } from '../api/inventory'
import { ApiError } from '../api/client'
import type { Part, PartInput, StockMovementType } from '../types'
import { ConflictBanner } from '../components/ConflictBanner'
import { EmptyState } from '../components/EmptyState'
import { formatMoney, formatQuantity } from '../utils/format'

// Number inputs are edited as text so a half-typed value ("12.") is not reformatted under the cursor.
interface PartForm {
  partNumber: string
  name: string
  description: string
  unit: string
  unitCost: string
  minimumStockLevel: string
  isActive: boolean
}

const emptyForm: PartForm = {
  partNumber: '',
  name: '',
  description: '',
  unit: 'pcs',
  unitCost: '0.00',
  minimumStockLevel: '0',
  isActive: true
}

function toPayload(form: PartForm): PartInput {
  return {
    partNumber: form.partNumber.trim(),
    name: form.name.trim(),
    description: form.description.trim() || undefined,
    unit: form.unit.trim(),
    unitCost: Number(form.unitCost),
    minimumStockLevel: Number(form.minimumStockLevel)
  }
}

interface Movement {
  part: Part
  type: StockMovementType
  quantity: string
  // Adjustments are entered as a direction plus a positive amount; the sign is applied on submit.
  direction: 'remove' | 'add'
  notes: string
}

export function InventoryPage() {
  const { auth, hasRole } = useAuth()
  const canEdit = hasRole(...INVENTORY_EDIT_ROLES)

  const [search, setSearch] = useState('')
  const [lowStockOnly, setLowStockOnly] = useState(false)
  const [includeInactive, setIncludeInactive] = useState(false)
  const [parts, setParts] = useState<Part[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const [pageError, setPageError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const [editing, setEditing] = useState<Part | 'new' | null>(null)
  const [form, setForm] = useState<PartForm>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)

  const [movement, setMovement] = useState<Movement | null>(null)
  const [movementError, setMovementError] = useState<string | null>(null)

  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setLoadError(null)
    const request = lowStockOnly
      // The low-stock list is small and active-only; the search box narrows it on the client.
      ? getLowStockParts(auth.token).then(rows => rows.filter(p => matches(p, search)))
      : getParts(auth.token, { search, includeInactive })

    request
      .then(rows => {
        if (!cancelled) setParts(rows)
      })
      .catch(err => {
        if (!cancelled) setLoadError(err instanceof ApiError ? err.message : 'Could not load parts.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, search, lowStockOnly, includeInactive, reloadKey])

  const reload = () => setReloadKey(k => k + 1)
  const filtered = search.trim() !== '' || lowStockOnly || includeInactive

  function startCreate() {
    setMovement(null)
    setForm(emptyForm)
    setFormError(null)
    setNotice(null)
    setEditing('new')
  }

  function startEdit(part: Part) {
    setMovement(null)
    setForm({
      partNumber: part.partNumber,
      name: part.name,
      description: part.description ?? '',
      unit: part.unit,
      unitCost: part.unitCost.toFixed(2),
      minimumStockLevel: String(part.minimumStockLevel),
      isActive: part.isActive
    })
    setFormError(null)
    setNotice(null)
    setEditing(part)
  }

  function startMovement(part: Part, type: StockMovementType) {
    setEditing(null)
    setMovementError(null)
    setNotice(null)
    setMovement({ part, type, quantity: '', direction: 'remove', notes: '' })
  }

  async function handleSavePart(event: FormEvent) {
    event.preventDefault()
    if (!auth || editing === null) return

    setSaving(true)
    setFormError(null)
    try {
      const payload = toPayload(form)
      const saved = editing === 'new'
        ? await createPart(auth.token, payload)
        : await updatePart(auth.token, editing.id, { ...payload, isActive: form.isActive, rowVersion: editing.rowVersion })
      setEditing(null)
      setNotice(`${saved.partNumber} saved.`)
      reload()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && editing !== 'new' && err.message.includes('changed by someone else')) {
        // Stale screen: close the form and show the latest values rather than keep editing old ones.
        setEditing(null)
        setPageError(err.message)
        reload()
      } else {
        setFormError(err instanceof ApiError ? err.message : 'Could not save this part.')
      }
    } finally {
      setSaving(false)
    }
  }

  async function handleMovement(event: FormEvent) {
    event.preventDefault()
    if (!auth || !movement) return

    const amount = Number(movement.quantity)
    const quantity = movement.type === 'Adjustment' && movement.direction === 'remove' ? -amount : amount

    setSaving(true)
    setMovementError(null)
    try {
      const entry = await recordStockMovement(auth.token, {
        partId: movement.part.id,
        transactionType: movement.type,
        quantity,
        notes: movement.notes.trim() || undefined
      })
      const verb = movement.type === 'Receive' ? 'Received' : 'Adjusted by'
      const after = entry.quantityOnHandAfter ?? movement.part.quantityOnHand + quantity
      setNotice(`${verb} ${formatQuantity(quantity)} ${entry.unit} of ${entry.partNumber}. On hand now ${formatQuantity(after)} ${entry.unit}.`)
      setMovement(null)
      reload()
    } catch (err) {
      setMovementError(err instanceof ApiError ? err.message : 'Could not record this stock movement.')
      // e.g. "Not enough stock": the on-hand figure the user was looking at is out of date.
      if (err instanceof ApiError && err.status === 409) reload()
    } finally {
      setSaving(false)
    }
  }

  const movementAmountValid = movement !== null && Number(movement.quantity) > 0
  const movementReasonMissing = movement?.type === 'Adjustment' && movement.notes.trim() === ''

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Inventory</h1>
          <p className="page-subtitle">
            Parts catalogue and stock on hand. Every change to stock is recorded in the{' '}
            <Link to="/inventory/history">transaction history</Link>.
          </p>
        </div>
        {canEdit && (
          <button className="btn btn-primary" onClick={startCreate}>
            New part
          </button>
        )}
      </div>

      <ConflictBanner message={pageError} />
      {notice && <p className="form-notice" role="status">{notice}</p>}

      {editing !== null && (
        <form className="panel form-panel" onSubmit={handleSavePart}>
          <h2 className="panel-title">{editing === 'new' ? 'New part' : `Edit ${editing.partNumber}`}</h2>

          <div className="field-grid">
            <label className="field">
              <span className="field-label">Part number</span>
              <input
                value={form.partNumber}
                onChange={e => setForm({ ...form, partNumber: e.target.value })}
                maxLength={60}
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Name</span>
              <input
                value={form.name}
                onChange={e => setForm({ ...form, name: e.target.value })}
                maxLength={200}
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Unit</span>
              <input
                value={form.unit}
                onChange={e => setForm({ ...form, unit: e.target.value })}
                maxLength={20}
                placeholder="pcs, L, m, kg…"
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Unit cost (RM)</span>
              <input
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                value={form.unitCost}
                onChange={e => setForm({ ...form, unitCost: e.target.value })}
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Minimum stock level</span>
              <input
                type="number"
                inputMode="decimal"
                min="0"
                step="0.001"
                value={form.minimumStockLevel}
                onChange={e => setForm({ ...form, minimumStockLevel: e.target.value })}
                required
              />
            </label>

            {editing !== 'new' && (
              <label className="field field-check">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={e => setForm({ ...form, isActive: e.target.checked })}
                />
                <span>Active (inactive parts cannot receive stock and are hidden from the list)</span>
              </label>
            )}

            <label className="field field-wide">
              <span className="field-label">Description</span>
              <input
                value={form.description}
                onChange={e => setForm({ ...form, description: e.target.value })}
                maxLength={1000}
              />
            </label>
          </div>

          {editing === 'new' && (
            <p className="form-hint">A new part starts with no stock. Use Receive to book in its first delivery.</p>
          )}

          {formError && <p className="form-error">{formError}</p>}

          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setEditing(null)}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save part'}
            </button>
          </div>
        </form>
      )}

      {movement && (
        <form className="panel form-panel" onSubmit={handleMovement}>
          <h2 className="panel-title">
            {movement.type === 'Receive' ? 'Receive stock' : 'Adjust stock'}: {movement.part.partNumber}
          </h2>
          <p className="form-hint">
            {movement.part.name} · on hand {formatQuantity(movement.part.quantityOnHand)} {movement.part.unit}
          </p>

          <div className="field-grid">
            {movement.type === 'Adjustment' && (
              <label className="field">
                <span className="field-label">Direction</span>
                <select
                  value={movement.direction}
                  onChange={e => setMovement({ ...movement, direction: e.target.value as Movement['direction'] })}
                >
                  <option value="remove">Remove stock</option>
                  <option value="add">Add stock</option>
                </select>
              </label>
            )}

            <label className="field">
              <span className="field-label">Quantity ({movement.part.unit})</span>
              <input
                type="number"
                inputMode="decimal"
                min="0.001"
                step="0.001"
                value={movement.quantity}
                onChange={e => setMovement({ ...movement, quantity: e.target.value })}
                required
                autoFocus
              />
            </label>

            <div className="field field-wide">
              <label className="field-label" htmlFor="movement-notes">
                {movement.type === 'Receive' ? 'Delivery note or reference (optional)' : 'Reason for adjusting'}
              </label>
              <textarea
                id="movement-notes"
                aria-describedby={movement.type === 'Adjustment' ? 'movement-notes-hint' : undefined}
                value={movement.notes}
                onChange={e => setMovement({ ...movement, notes: e.target.value })}
                maxLength={500}
                rows={2}
                required={movement.type === 'Adjustment'}
              />
              {movement.type === 'Adjustment' && (
                <span id="movement-notes-hint" className="form-hint">
                  Required. The ledger cannot be edited, so this reason is the permanent record of why stock changed.
                </span>
              )}
            </div>
          </div>

          {movementError && <p className="form-error" role="alert">{movementError}</p>}

          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setMovement(null)} disabled={saving}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={saving || !movementAmountValid || movementReasonMissing}
            >
              {saving ? 'Saving…' : movement.type === 'Receive' ? 'Confirm receipt' : 'Confirm adjustment'}
            </button>
          </div>
        </form>
      )}

      <div className="toolbar">
        <label className="field field-inline">
          <span className="field-label">Search</span>
          <input
            type="search"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Part number or name"
          />
        </label>
        <label className="field field-check">
          <input type="checkbox" checked={lowStockOnly} onChange={e => setLowStockOnly(e.target.checked)} />
          <span>Low stock only</span>
        </label>
        <label className="field field-check">
          <input
            type="checkbox"
            checked={includeInactive}
            onChange={e => setIncludeInactive(e.target.checked)}
            disabled={lowStockOnly}
          />
          <span>Show inactive parts</span>
        </label>
      </div>

      {loadError && <p className="form-error">{loadError}</p>}

      {loading ? (
        <p className="page-subtitle">Loading parts…</p>
      ) : parts.length === 0 ? (
        <EmptyState
          title={lowStockOnly ? 'Nothing is low on stock' : filtered ? 'No parts match these filters' : 'No parts yet'}
          description={lowStockOnly
            ? 'Every active part is above its minimum stock level.'
            : filtered
              ? 'Try a different search.'
              : canEdit ? 'Add the first part to start tracking stock.' : 'Ask the warehouse to add parts.'}
        />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Part no.</th>
              <th>Name</th>
              <th className="num">On hand</th>
              <th className="num">Minimum</th>
              <th className="num">Unit cost</th>
              <th>Stock</th>
              <th aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {parts.map(part => (
              <tr key={part.id} className={part.isLowStock ? 'row-warn' : undefined}>
                <td className="mono">{part.partNumber}</td>
                <td>{part.name}</td>
                <td className="num mono">{formatQuantity(part.quantityOnHand)} {part.unit}</td>
                <td className="num mono">{formatQuantity(part.minimumStockLevel)}</td>
                <td className="num mono">{formatMoney(part.unitCost)}</td>
                <td>
                  {!part.isActive
                    ? <span className="status-badge tone-grey">Inactive</span>
                    : part.isLowStock
                      ? <span className="status-badge tone-red">Low stock</span>
                      : <span className="status-badge tone-green">OK</span>}
                </td>
                <td>
                  <div className="row-actions">
                    {canEdit && part.isActive && (
                      <button className="btn btn-link" onClick={() => startMovement(part, 'Receive')}>
                        Receive
                      </button>
                    )}
                    {canEdit && (
                      <button className="btn btn-link" onClick={() => startMovement(part, 'Adjustment')}>
                        Adjust
                      </button>
                    )}
                    {canEdit && (
                      <button className="btn btn-link" onClick={() => startEdit(part)}>
                        Edit
                      </button>
                    )}
                    <Link className="btn-link" to={`/inventory/history?partId=${part.id}`}>History</Link>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

function matches(part: Part, search: string): boolean {
  const term = search.trim().toLowerCase()
  return term === '' || part.partNumber.toLowerCase().includes(term) || part.name.toLowerCase().includes(term)
}
