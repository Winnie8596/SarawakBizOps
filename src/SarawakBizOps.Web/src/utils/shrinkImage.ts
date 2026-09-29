/** The API's per-photo limit (PhotoRules.MaxBytes). */
export const MAX_PHOTO_BYTES = 8 * 1024 * 1024

const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp']

/** Longest side of a shrunk photo: plenty to read a nameplate or see a worn seal. */
const MAX_SIDE = 2048

/**
 * Most phone photos go up as they are. One that is over the API's limit, or in a format the API does not
 * take (e.g. HEIC from some iPhones, when the browser can decode it), is redrawn as a JPEG no larger than
 * 2048 px a side. When the browser cannot do that, the original is returned and the API explains the refusal.
 */
export async function shrinkImageIfNeeded(file: File): Promise<Blob> {
  if (file.size <= MAX_PHOTO_BYTES && ACCEPTED_TYPES.includes(file.type)) return file
  if (typeof createImageBitmap !== 'function') return file

  try {
    const bitmap = await createImageBitmap(file)
    const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(bitmap.width * scale)
    canvas.height = Math.round(bitmap.height * scale)
    const context = canvas.getContext('2d')
    if (!context) return file
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    bitmap.close()

    const jpeg = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85))
    if (!jpeg) return file
    return new File([jpeg], file.name.replace(/\.[^.]*$/, '') + '.jpg', { type: 'image/jpeg' })
  } catch {
    return file
  }
}
