import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { workOrder } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

// jsdom has no 2D canvas: give the pad a context that accepts drawing calls, and a toBlob that returns a PNG.
const drawnPng = new Blob(['drawn signature'], { type: 'image/png' })
const storedPng = new Blob(['stored signature'], { type: 'image/png' })

beforeEach(() => {
  const context = new Proxy({}, { get: () => vi.fn(), set: () => true })
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(callback => callback(drawnPng))
})

afterEach(() => {
  vi.restoreAllMocks()
})

function signOnThePad() {
  const pad = screen.getByRole('img', { name: /Signature pad/ })
  fireEvent.pointerDown(pad, { pointerId: 1, clientX: 20, clientY: 40 })
  fireEvent.pointerMove(pad, { pointerId: 1, clientX: 80, clientY: 60 })
  fireEvent.pointerMove(pad, { pointerId: 1, clientX: 140, clientY: 30 })
  fireEvent.pointerUp(pad, { pointerId: 1 })
}

const inProgress = { status: 'InProgress' as const, startedAt: '2026-09-21T01:00:00', diagnosis: 'Loose belt', workPerformed: 'Retensioned it' }

describe('Signature step (technician)', () => {
  it('refuses to complete until the customer has signed, then uploads the drawing as a PNG with the rowVersion', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder(inProgress) },
      'POST /work-orders/41/signature': {
        body: workOrder({ ...inProgress, hasSignature: true, signedAt: '2026-09-21T02:00:00', rowVersion: 'AAAAAAAAC00=' })
      },
      'GET /work-orders/41/signature': { body: storedPng }
    })
    renderApp('/tech/jobs/41')

    const complete = await screen.findByRole('button', { name: 'Complete job' })
    expect(complete).toBeDisabled()
    expect(screen.getByText('The customer must sign before the job can be completed.')).toBeInTheDocument()
    const save = screen.getByRole('button', { name: 'Save signature' })
    expect(save).toBeDisabled()

    signOnThePad()
    await userEvent.click(save)

    expect(await screen.findByRole('img', { name: 'Customer signature' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /Signature pad/ })).not.toBeInTheDocument()
    expect(complete).toBeEnabled()

    const [, init] = callsTo(fetchMock, 'POST', '/work-orders/41/signature')[0]
    const form = init?.body as FormData
    expect(form).toBeInstanceOf(FormData)
    expect(form.get('rowVersion')).toBe('AAAAAAAAB9E=')
    expect(form.get('file')).toBeInstanceOf(Blob)
    // The browser must set multipart/form-data with its boundary, so the client sets no Content-Type itself.
    expect(new Headers(init?.headers).has('Content-Type')).toBe(false)
  })

  it('undoes and clears strokes before anything is saved', async () => {
    signInAs('Technician')
    stubApi({ 'GET /work-orders/41': { body: workOrder(inProgress) } })
    renderApp('/tech/jobs/41')

    await screen.findByRole('button', { name: 'Complete job' })
    signOnThePad()
    expect(screen.getByRole('button', { name: 'Save signature' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getByRole('button', { name: 'Save signature' })).toBeDisabled()

    signOnThePad()
    signOnThePad()
    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))
    expect(screen.getByRole('button', { name: 'Save signature' })).toBeDisabled()
    expect(screen.getByText('Sign inside the box.')).toBeInTheDocument()
  })

  it('keeps the drawing and shows the reason when the upload is refused', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder(inProgress) },
      'POST /work-orders/41/signature': {
        status: 400,
        body: { title: 'Bad request', detail: 'The signature must be a PNG image.' }
      }
    })
    renderApp('/tech/jobs/41')

    await screen.findByRole('button', { name: 'Complete job' })
    signOnThePad()
    await userEvent.click(screen.getByRole('button', { name: 'Save signature' }))

    expect(await screen.findByText('The signature must be a PNG image.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save signature' })).toBeEnabled() // strokes kept for a retry
    expect(screen.getByRole('button', { name: 'Complete job' })).toBeDisabled()
  })

  it('lets the customer sign again, or keep the stored signature', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ ...inProgress, hasSignature: true, signedAt: '2026-09-21T02:00:00' }) },
      'GET /work-orders/41/signature': { body: storedPng }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Sign again' }))
    expect(screen.getByRole('img', { name: /Signature pad/ })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Keep the old one' }))
    expect(await screen.findByRole('img', { name: 'Customer signature' })).toBeInTheDocument()
  })

  it('shows the signature read-only once the job is completed', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({ ...inProgress, status: 'Completed', hasSignature: true, signedAt: '2026-09-21T02:00:00' })
      },
      'GET /work-orders/41/signature': { body: storedPng }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByRole('img', { name: 'Customer signature' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /Signature pad/ })).not.toBeInTheDocument()
  })
})

describe('Signature on the office work-order page', () => {
  it('shows the Manager the stored signature, fetched through the API with the token', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': {
        body: workOrder({ ...inProgress, status: 'Completed', hasSignature: true, signedAt: '2026-09-21T02:00:00' })
      },
      'GET /work-orders/41/signature': { body: storedPng }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByRole('img', { name: 'Customer signature' })).toBeInTheDocument()
    expect(screen.getByText('Customer signed')).toBeInTheDocument() // on the timeline
    const [, init] = callsTo(fetchMock, 'GET', '/work-orders/41/signature')[0]
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer test-token')
  })

  it('says when the customer has not signed yet', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({ 'GET /work-orders/41': { body: workOrder(inProgress) } })
    renderApp('/work-orders/41')

    expect(await screen.findByText(/Not signed yet/)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/work-orders/41/signature')).toHaveLength(0)
  })

  it('explains when the signature image cannot be loaded', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({ ...inProgress, status: 'Completed', hasSignature: true, signedAt: '2026-09-21T02:00:00' })
      },
      'GET /work-orders/41/signature': { status: 404, body: { title: 'Not found', detail: 'This job has no customer signature yet.' } }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByText('This job has no customer signature yet.')).toBeInTheDocument()
  })
})
