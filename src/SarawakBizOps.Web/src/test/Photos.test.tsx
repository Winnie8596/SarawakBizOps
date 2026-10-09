import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { workOrder, workOrderPhoto } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'
import type { WorkOrder } from '../types'

// Photo uploads report progress, which fetch cannot do, so the app sends them with XMLHttpRequest. This fake
// records each upload, fires one progress event, then answers with whatever the test's handler returns —
// which may be a promise the test resolves later, to look at the screen mid-upload.
interface UploadRequest {
  url: string
  form: FormData
  headers: Record<string, string>
}

interface UploadResponse {
  status?: number
  body: unknown
}

class FakeXhr {
  static handler: (request: UploadRequest) => UploadResponse | Promise<UploadResponse>
  static requests: UploadRequest[] = []

  upload: { onprogress: ((event: ProgressEvent) => void) | null } = { onprogress: null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  status = 0
  responseText = ''
  private url = ''
  private headers: Record<string, string> = {}
  private responseContentType = ''

  open(_method: string, url: string) {
    this.url = url
  }

  setRequestHeader(name: string, value: string) {
    this.headers[name] = value
  }

  getResponseHeader(name: string) {
    return name.toLowerCase() === 'content-type' ? this.responseContentType : null
  }

  send(form: FormData) {
    const request = { url: this.url, form, headers: this.headers }
    FakeXhr.requests.push(request)
    setTimeout(async () => {
      this.upload.onprogress?.({ lengthComputable: true, loaded: 40, total: 100 } as ProgressEvent)
      const { status = 200, body } = await FakeXhr.handler(request)
      this.status = status
      this.responseText = JSON.stringify(body)
      this.responseContentType = status >= 400 ? 'application/problem+json' : 'application/json'
      this.onload?.()
    }, 0)
  }
}

const photoBlob = new Blob(['stored photo'], { type: 'image/jpeg' })
const inProgress = { status: 'InProgress' as const, startedAt: '2026-09-21T01:00:00' }

function jpeg(name: string) {
  return new File(['fake jpeg bytes'], name, { type: 'image/jpeg' })
}

beforeEach(() => {
  FakeXhr.requests = []
  vi.stubGlobal('XMLHttpRequest', FakeXhr)
  // The InProgress page also shows the signature pad; jsdom has no 2D canvas.
  const context = new Proxy({}, { get: () => vi.fn(), set: () => true })
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Photos step (technician)', () => {
  it('uploads the chosen photos one by one, each with the rowVersion the last one returned, showing progress', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder(inProgress) },
      'GET /work-orders/41/photos/801': { body: photoBlob },
      'GET /work-orders/41/photos/802': { body: photoBlob }
    })
    let releaseFirst: () => void = () => {}
    const firstHeld = new Promise<void>(resolve => {
      releaseFirst = resolve
    })
    const afterFirst = workOrder({ ...inProgress, photos: [workOrderPhoto({ id: 801 })], rowVersion: 'AAAAAAAAC01=' })
    const afterSecond = workOrder({
      ...inProgress,
      photos: [workOrderPhoto({ id: 801 }), workOrderPhoto({ id: 802 })],
      rowVersion: 'AAAAAAAAC02='
    })
    FakeXhr.handler = async () => {
      if (FakeXhr.requests.length === 1) {
        await firstHeld
        return { body: afterFirst }
      }
      return { body: afterSecond }
    }
    renderApp('/tech/jobs/41')

    expect(await screen.findByText(/No photos yet/)).toBeInTheDocument()
    await userEvent.upload(screen.getByLabelText('Choose photos'), [jpeg('fault.jpg'), jpeg('repair.jpg')])

    // Mid-upload: progress for the first, the second waits its turn, and nothing else can run.
    expect(await screen.findByText('Uploading 40%')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Uploading fault.jpg' })).toBeInTheDocument()
    expect(screen.getByText('Waiting…')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uploading photos…' })).toBeDisabled()
    expect(FakeXhr.requests).toHaveLength(1)

    releaseFirst()

    expect(await screen.findByRole('img', { name: 'Job photo 2' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Job photo 1' })).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Complete job' })).toBeInTheDocument()

    const [first, second] = FakeXhr.requests
    expect(first.url).toMatch(/\/api\/work-orders\/41\/photos$/)
    expect(first.headers.Authorization).toBe('Bearer test-token')
    expect(first.form.get('rowVersion')).toBe('AAAAAAAAB9E=')
    expect((first.form.get('file') as File).name).toBe('fault.jpg')
    expect(second.form.get('rowVersion')).toBe('AAAAAAAAC01=') // carried forward from the first upload
    expect((second.form.get('file') as File).name).toBe('repair.jpg')
  })

  it('shows why an upload failed and uploads it again on Retry', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder(inProgress) },
      'GET /work-orders/41/photos/801': { body: photoBlob }
    })
    FakeXhr.handler = () =>
      FakeXhr.requests.length === 1
        ? { status: 400, body: { title: 'Bad request', detail: 'A photo must be a JPEG or PNG image.' } }
        : { body: workOrder({ ...inProgress, photos: [workOrderPhoto()], rowVersion: 'AAAAAAAAC01=' }) }
    renderApp('/tech/jobs/41')

    await userEvent.upload(await screen.findByLabelText('Take photo'), jpeg('fault.jpg'))

    expect(await screen.findByText('A photo must be a JPEG or PNG image.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Complete job' })).toBeInTheDocument() // the page is usable again

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('img', { name: 'Job photo 1' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    expect(FakeXhr.requests).toHaveLength(2)
  })

  it('reloads the job when an upload hits a conflict, and a failed photo can be discarded', async () => {
    signInAs('Technician')
    const cancelled: WorkOrder = workOrder({ status: 'Cancelled', cancellationReason: 'Customer withdrew' })
    const fetchMock = stubApi({ 'GET /work-orders/41': { body: workOrder(inProgress) } })
    FakeXhr.handler = () => {
      stubApi({ 'GET /work-orders/41': { body: cancelled } })
      return {
        status: 409,
        body: { title: 'Conflict', detail: 'This job is Cancelled and is locked against further changes.' }
      }
    }
    renderApp('/tech/jobs/41')

    await userEvent.upload(await screen.findByLabelText('Take photo'), jpeg('fault.jpg'))

    expect(await screen.findByText('This job is Cancelled and is locked against further changes.')).toBeInTheDocument()
    expect(await screen.findByText('Cancelled — read only')).toBeInTheDocument()
    expect(screen.queryByLabelText('Take photo')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeDisabled()
    expect(callsTo(fetchMock, 'GET', '/work-orders/41')).toHaveLength(1) // the reload went to the new stub

    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })

  it('removes a photo before completion, sending the rowVersion', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ ...inProgress, photos: [workOrderPhoto()] }) },
      'GET /work-orders/41/photos/801': { body: photoBlob },
      'DELETE /work-orders/41/photos/801': { body: workOrder({ ...inProgress, rowVersion: 'AAAAAAAAC03=' }) }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByRole('img', { name: 'Job photo 1' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Remove job photo 1' }))

    expect(await screen.findByText(/No photos yet/)).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'DELETE', '/work-orders/41/photos/801')[0]
    expect(JSON.parse(init?.body as string)).toEqual({ rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows the photos read-only once the job is completed', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          ...inProgress,
          status: 'Completed',
          hasSignature: true,
          signedAt: '2026-09-21T02:00:00',
          photos: [workOrderPhoto()]
        })
      },
      'GET /work-orders/41/photos/801': { body: photoBlob },
      'GET /work-orders/41/signature': { body: new Blob(['sig'], { type: 'image/png' }) }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByRole('img', { name: 'Job photo 1' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Remove/ })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Take photo')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Choose photos')).not.toBeInTheDocument()
  })
})

describe('Photos on the office work-order page', () => {
  it('shows the Manager a gallery, each image fetched through the API with the token', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': {
        body: workOrder({ ...inProgress, photos: [workOrderPhoto({ id: 801 }), workOrderPhoto({ id: 802 })] })
      },
      'GET /work-orders/41/photos/801': { body: photoBlob },
      'GET /work-orders/41/photos/802': { body: photoBlob }
    })
    renderApp('/work-orders/41')

    const photos = (await screen.findByRole('heading', { name: 'Photos' })).parentElement!
    expect(await within(photos).findByRole('img', { name: 'Job photo 1' })).toBeInTheDocument()
    expect(await within(photos).findByRole('img', { name: 'Job photo 2' })).toBeInTheDocument()
    expect(within(photos).getAllByRole('link', { name: /Job photo/ })).toHaveLength(2) // opens full size
    expect(within(photos).queryByRole('button', { name: /Remove/ })).not.toBeInTheDocument()
    await waitFor(() => expect(callsTo(fetchMock, 'GET', '/work-orders/41/photos/802')).toHaveLength(1))
    const [, init] = callsTo(fetchMock, 'GET', '/work-orders/41/photos/801')[0]
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer test-token')
  })

  it('says when no photos were taken', async () => {
    signInAs('Manager')
    stubApi({ 'GET /work-orders/41': { body: workOrder(inProgress) } })
    renderApp('/work-orders/41')

    expect(await screen.findByText('No photos taken on this job.')).toBeInTheDocument()
  })

  it('explains when a photo cannot be loaded', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ ...inProgress, photos: [workOrderPhoto()] }) },
      'GET /work-orders/41/photos/801': {
        status: 404,
        body: { title: 'Not found', detail: 'That photo could not be found.' }
      }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByText('That photo could not be found.')).toBeInTheDocument()
  })
})
