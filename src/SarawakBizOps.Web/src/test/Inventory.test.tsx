import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { inventoryTransaction, part } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

const relay = part()
const belt = part({
  id: 302, partNumber: 'BLT-CNV-600', name: 'Conveyor belt, 600 mm', unit: 'm',
  unitCost: 88.5, quantityOnHand: 0, minimumStockLevel: 10, isLowStock: true
})

describe('Inventory list', () => {
  it('shows parts with RM prices and highlights the ones low on stock', async () => {
    signInAs('WarehouseStaff')
    stubApi({ 'GET /parts': { body: [belt, relay] } })
    renderApp('/inventory')

    const beltRow = (await screen.findByText('BLT-CNV-600')).closest('tr')!
    expect(beltRow).toHaveClass('row-warn')
    expect(within(beltRow).getByText('Low stock')).toBeInTheDocument()
    expect(within(beltRow).getByText('RM 88.50')).toBeInTheDocument()
    expect(within(beltRow).getByText('0 m')).toBeInTheDocument()

    const relayRow = screen.getByText('REL-OVL-25A').closest('tr')!
    expect(relayRow).not.toHaveClass('row-warn')
    expect(within(relayRow).getByText('RM 1,450.50')).toBeInTheDocument()
    expect(within(relayRow).getByText('OK')).toBeInTheDocument()
  })

  it('switches to the low-stock endpoint when "Low stock only" is ticked', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /parts': { body: [belt, relay] },
      'GET /parts/low-stock': { body: [belt] }
    })
    renderApp('/inventory')

    await screen.findByText('REL-OVL-25A')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Low stock only' }))

    await screen.findByText('BLT-CNV-600')
    expect(screen.queryByText('REL-OVL-25A')).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/parts/low-stock')).toHaveLength(1)
  })

  it('shows an empty state before any part exists', async () => {
    signInAs('WarehouseStaff')
    stubApi({ 'GET /parts': { body: [] } })
    renderApp('/inventory')

    expect(await screen.findByText('No parts yet')).toBeInTheDocument()
  })

  it('shows an error when the parts cannot be loaded', async () => {
    signInAs('WarehouseStaff')
    stubApi({ 'GET /parts': { status: 500, body: { title: 'Server error', detail: 'An unexpected error occurred. Please try again.' } } })
    renderApp('/inventory')

    expect(await screen.findByText('An unexpected error occurred. Please try again.')).toBeInTheDocument()
  })

  it('gives a Manager a read-only view: no new part, receive, adjust or edit', async () => {
    signInAs('Manager')
    stubApi({ 'GET /parts': { body: [relay] } })
    renderApp('/inventory')

    const row = (await screen.findByText('REL-OVL-25A')).closest('tr')!
    expect(screen.queryByRole('button', { name: 'New part' })).not.toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'History' })).toHaveAttribute('href', '/inventory/history?partId=301')
  })
})

describe('Warehouse stock movements', () => {
  it('receives a delivery and reports the new stock level', async () => {
    signInAs('WarehouseStaff')
    let received = false
    const fetchMock = stubApi({
      'GET /parts': () => ({ body: [received ? { ...relay, quantityOnHand: 16 } : relay] }),
      'POST /inventory-transactions': () => {
        received = true
        return { status: 201, body: inventoryTransaction({ quantity: 10, quantityOnHandAfter: 16 }) }
      }
    })
    renderApp('/inventory')

    const row = (await screen.findByText('REL-OVL-25A')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Receive' }))
    await userEvent.type(screen.getByRole('spinbutton', { name: 'Quantity (pcs)' }), '10')
    await userEvent.type(screen.getByRole('textbox', { name: 'Delivery note or reference (optional)' }), 'DN-777')
    await userEvent.click(screen.getByRole('button', { name: 'Confirm receipt' }))

    expect(await screen.findByText('Received 10 pcs of REL-OVL-25A. On hand now 16 pcs.')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/inventory-transactions')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ partId: 301, transactionType: 'Receive', quantity: 10, notes: 'DN-777' })
    expect(await screen.findByText('16 pcs')).toBeInTheDocument()
  })

  it('will not submit an adjustment until a reason is given, and sends removals as a negative quantity', async () => {
    signInAs('WarehouseStaff')
    const fetchMock = stubApi({
      'GET /parts': { body: [relay] },
      'POST /inventory-transactions': {
        status: 201,
        body: inventoryTransaction({ transactionType: 'Adjustment', quantity: -2, quantityOnHandAfter: 4 })
      }
    })
    renderApp('/inventory')

    const row = (await screen.findByText('REL-OVL-25A')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Adjust' }))
    await userEvent.type(screen.getByRole('spinbutton', { name: 'Quantity (pcs)' }), '2')

    const confirm = screen.getByRole('button', { name: 'Confirm adjustment' })
    expect(confirm).toBeDisabled()

    await userEvent.type(screen.getByRole('textbox', { name: 'Reason for adjusting' }), 'Stocktake: two short')
    expect(confirm).toBeEnabled()
    await userEvent.click(confirm)

    expect(await screen.findByText('Adjusted by -2 pcs of REL-OVL-25A. On hand now 4 pcs.')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/inventory-transactions')[0]
    expect(JSON.parse(String(init?.body))).toEqual({
      partId: 301, transactionType: 'Adjustment', quantity: -2, notes: 'Stocktake: two short'
    })
  })

  it('shows the API refusal when an adjustment would take stock below zero', async () => {
    signInAs('WarehouseStaff')
    stubApi({
      'GET /parts': { body: [relay] },
      'POST /inventory-transactions': {
        status: 409,
        body: { title: 'Conflict', detail: 'Not enough stock: only 6 pcs on hand, so it cannot be reduced by 9.' }
      }
    })
    renderApp('/inventory')

    const row = (await screen.findByText('REL-OVL-25A')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Adjust' }))
    await userEvent.type(screen.getByRole('spinbutton', { name: 'Quantity (pcs)' }), '9')
    await userEvent.type(screen.getByRole('textbox', { name: 'Reason for adjusting' }), 'Write-off')
    await userEvent.click(screen.getByRole('button', { name: 'Confirm adjustment' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Not enough stock: only 6 pcs on hand')
  })

  it('creates a part with no stock field in the request', async () => {
    signInAs('WarehouseStaff')
    const fetchMock = stubApi({
      'GET /parts': { body: [] },
      'POST /parts': { status: 201, body: part({ id: 303, partNumber: 'BRG-6205', quantityOnHand: 0 }) }
    })
    renderApp('/inventory')

    await userEvent.click(await screen.findByRole('button', { name: 'New part' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Part number' }), 'BRG-6205')
    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Ball bearing')
    const cost = screen.getByRole('spinbutton', { name: 'Unit cost (RM)' })
    await userEvent.clear(cost)
    await userEvent.type(cost, '18.40')
    const minimum = screen.getByRole('spinbutton', { name: 'Minimum stock level' })
    await userEvent.clear(minimum)
    await userEvent.type(minimum, '20')
    await userEvent.click(screen.getByRole('button', { name: 'Save part' }))

    expect(await screen.findByText('BRG-6205 saved.')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/parts')[0]
    expect(JSON.parse(String(init?.body))).toEqual({
      partNumber: 'BRG-6205', name: 'Ball bearing', unit: 'pcs', unitCost: 18.4, minimumStockLevel: 20
    })
  })

  it('closes a stale edit on a 409 and shows the latest values', async () => {
    signInAs('WarehouseStaff')
    let conflicted = false
    stubApi({
      'GET /parts': () => ({ body: [conflicted ? { ...relay, name: 'Renamed elsewhere' } : relay] }),
      'PUT /parts/301': () => {
        conflicted = true
        return { status: 409, body: { title: 'Conflict', detail: 'This part was changed by someone else. Refresh and try again.' } }
      }
    })
    renderApp('/inventory')

    const row = (await screen.findByText('REL-OVL-25A')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save part' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This part was changed by someone else.')
    expect(await screen.findByText('Renamed elsewhere')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save part' })).not.toBeInTheDocument()
  })
})

describe('Transaction history', () => {
  it('lists movements with signed quantities and filters by the part from the link', async () => {
    signInAs('WarehouseStaff')
    const fetchMock = stubApi({
      'GET /parts': { body: [relay, belt] },
      'GET /inventory-transactions': {
        body: [
          inventoryTransaction({ id: 902, transactionType: 'Adjustment', quantity: -2, notes: 'Stocktake: two short' }),
          inventoryTransaction()
        ]
      }
    })
    renderApp('/inventory/history?partId=301')

    expect(await screen.findByText('Stocktake: two short')).toBeInTheDocument()
    expect(screen.getByText('-2 pcs')).toBeInTheDocument()
    expect(screen.getByText('+6 pcs')).toBeInTheDocument()
    const [input] = callsTo(fetchMock, 'GET', '/inventory-transactions')[0]
    expect(new URL(String(input)).searchParams.get('partId')).toBe('301')
    // Read-only by design: nothing on the page edits or deletes a ledger row.
    expect(within(screen.getByRole('main')).queryByRole('button')).not.toBeInTheDocument()
  })

  it('turns an MYT date range into the matching UTC window for the API', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /parts': { body: [relay] },
      'GET /inventory-transactions': { body: [] }
    })
    renderApp('/inventory/history')

    await screen.findByText('No stock movements yet')
    await userEvent.type(screen.getByLabelText('From (MYT)'), '2026-09-01')
    await userEvent.type(screen.getByLabelText('To (MYT)'), '2026-09-28')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Type' }), 'Receive')

    expect(await screen.findByText('No transactions match these filters')).toBeInTheDocument()
    const calls = callsTo(fetchMock, 'GET', '/inventory-transactions')
    const params = new URL(String(calls[calls.length - 1][0])).searchParams
    expect(params.get('from')).toBe('2026-08-31T16:00:00.000Z')
    expect(params.get('to')).toBe('2026-09-28T16:00:00.000Z') // the end of 28 Sep MYT, exclusive
    expect(params.get('type')).toBe('Receive')
  })
})
