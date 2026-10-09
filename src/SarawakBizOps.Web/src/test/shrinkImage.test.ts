import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MAX_PHOTO_BYTES, shrinkImageIfNeeded } from '../utils/shrinkImage'

// jsdom decodes no images and has no 2D canvas: a bitmap of a known size, a context that accepts drawImage, and
// a toBlob that returns a JPEG stand in for the browser.
const redrawn = new Blob(['jpeg bytes'], { type: 'image/jpeg' })
let drawn: { width: number; height: number } | null = null

beforeEach(() => {
  drawn = null
  vi.stubGlobal(
    'createImageBitmap',
    vi.fn(async () => ({ width: 4000, height: 3000, close: vi.fn() }))
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement) {
    return {
      drawImage: () => {
        drawn = { width: this.width, height: this.height }
      }
    } as unknown as CanvasRenderingContext2D
  } as unknown as typeof HTMLCanvasElement.prototype.getContext)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(callback => callback(redrawn))
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('shrinkImageIfNeeded', () => {
  it('sends a JPEG or PNG within the limit as it is', async () => {
    const jpeg = new File(['x'], 'pump.jpg', { type: 'image/jpeg' })
    const png = new File(['x'], 'plate.png', { type: 'image/png' })

    expect(await shrinkImageIfNeeded(jpeg)).toBe(jpeg)
    expect(await shrinkImageIfNeeded(png)).toBe(png)
    expect(createImageBitmap).not.toHaveBeenCalled()
  })

  it('redraws a WebP as a JPEG, which the API stores and the report can show', async () => {
    const webp = new File(['x'], 'IMG_0042.webp', { type: 'image/webp' })

    const result = await shrinkImageIfNeeded(webp)

    expect(result).toBeInstanceOf(File)
    expect((result as File).name).toBe('IMG_0042.jpg')
    expect(result.type).toBe('image/jpeg')
    expect(drawn).toEqual({ width: 2048, height: 1536 }) // and no larger than 2048 px a side
  })

  it('shrinks a JPEG over the API limit', async () => {
    const big = new File([new Uint8Array(MAX_PHOTO_BYTES + 1)], 'big.jpg', { type: 'image/jpeg' })

    expect((await shrinkImageIfNeeded(big)).type).toBe('image/jpeg')
    expect(drawn).not.toBeNull()
  })

  it('sends the original when the browser cannot decode it, so the API can explain the refusal', async () => {
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(async () => {
        throw new Error('cannot decode')
      })
    )
    const webp = new File(['x'], 'odd.webp', { type: 'image/webp' })

    expect(await shrinkImageIfNeeded(webp)).toBe(webp)
  })
})
