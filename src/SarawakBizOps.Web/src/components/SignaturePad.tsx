import { useCallback, useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'

type Point = { x: number; y: number }

const PAD_HEIGHT = 180
const INK = '#1c2530'
const LINE_WIDTH = 2.5

interface SignaturePadProps {
  /** Receives the drawing as a PNG. Resolves true once stored, so the pad knows it may clear. */
  onSave: (png: Blob) => Promise<boolean>
  busy: boolean
  /** Shown next to Save when an earlier signature exists (re-signing), to go back without changing it. */
  onCancel?: () => void
}

/**
 * Hand-rolled signature pad (Phase 8, PRD D-06: no canvas library): pointer events cover touch, mouse and pen
 * alike. Strokes are kept as points in CSS pixels, so Undo simply redraws the rest, and a resize (rotating the
 * phone) redraws them at the new size without losing the signature. The canvas is painted white under the ink
 * so the exported PNG reads the same on any background.
 */
export function SignaturePad({ onSave, busy, onCancel }: SignaturePadProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const strokesRef = useRef<Point[][]>([])
  const drawingRef = useRef(false)
  const [strokeCount, setStrokeCount] = useState(0)

  const context = () => canvasRef.current?.getContext('2d') ?? null

  const redraw = useCallback(() => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx) return

    const ratio = window.devicePixelRatio || 1
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width / ratio, canvas.height / ratio)
    ctx.strokeStyle = INK
    ctx.fillStyle = INK
    ctx.lineWidth = LINE_WIDTH
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'

    for (const stroke of strokesRef.current) {
      if (stroke.length === 1) {
        // A tap is a dot (e.g. the dot on an "i").
        ctx.beginPath()
        ctx.arc(stroke[0].x, stroke[0].y, LINE_WIDTH / 2, 0, Math.PI * 2)
        ctx.fill()
        continue
      }
      ctx.beginPath()
      ctx.moveTo(stroke[0].x, stroke[0].y)
      for (const point of stroke.slice(1)) ctx.lineTo(point.x, point.y)
      ctx.stroke()
    }
  }, [])

  // Size the backing store to the element's real size × devicePixelRatio, so lines stay crisp on phones.
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const resize = () => {
      const ratio = window.devicePixelRatio || 1
      const width = canvas.getBoundingClientRect().width || canvas.parentElement?.clientWidth || 320
      canvas.width = Math.round(width * ratio)
      canvas.height = Math.round(PAD_HEIGHT * ratio)
      redraw()
    }

    resize()
    window.addEventListener('resize', resize)
    return () => window.removeEventListener('resize', resize)
  }, [redraw])

  function pointFrom(event: ReactPointerEvent<HTMLCanvasElement>): Point {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  function handlePointerDown(event: ReactPointerEvent<HTMLCanvasElement>) {
    if (busy) return
    event.preventDefault()
    event.currentTarget.setPointerCapture?.(event.pointerId)
    drawingRef.current = true
    strokesRef.current.push([pointFrom(event)])
    setStrokeCount(strokesRef.current.length)
    redraw()
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLCanvasElement>) {
    if (!drawingRef.current) return
    event.preventDefault()
    const stroke = strokesRef.current[strokesRef.current.length - 1]
    const from = stroke[stroke.length - 1]
    const to = pointFrom(event)
    stroke.push(to)

    // Draw just the new segment; a full redraw per move would lag on a slow phone.
    const ctx = context()
    if (!ctx) return
    ctx.beginPath()
    ctx.moveTo(from.x, from.y)
    ctx.lineTo(to.x, to.y)
    ctx.stroke()
  }

  function handlePointerUp() {
    drawingRef.current = false
  }

  function handleUndo() {
    strokesRef.current.pop()
    setStrokeCount(strokesRef.current.length)
    redraw()
  }

  function handleClear() {
    strokesRef.current = []
    setStrokeCount(0)
    redraw()
  }

  async function handleSave() {
    const canvas = canvasRef.current
    if (!canvas || strokeCount === 0) return
    const png = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png'))
    if (!png) return
    if (await onSave(png)) handleClear()
  }

  return (
    <div className="signature-pad">
      <canvas
        ref={canvasRef}
        className="signature-pad-canvas"
        style={{ height: PAD_HEIGHT }}
        role="img"
        aria-label="Signature pad: the customer signs here with a finger or stylus"
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={handlePointerUp}
        onPointerLeave={handlePointerUp}
      />
      <p className="form-hint">{strokeCount === 0 ? 'Sign inside the box.' : 'Happy with it? Save the signature.'}</p>
      <div className="signature-pad-actions">
        <button type="button" className="btn btn-ghost" onClick={handleUndo} disabled={busy || strokeCount === 0}>
          Undo
        </button>
        <button type="button" className="btn btn-ghost" onClick={handleClear} disabled={busy || strokeCount === 0}>
          Clear
        </button>
        {onCancel && (
          <button type="button" className="btn btn-ghost" onClick={onCancel} disabled={busy}>
            Keep the old one
          </button>
        )}
        <button type="button" className="btn btn-primary" onClick={handleSave} disabled={busy || strokeCount === 0}>
          {busy ? 'Saving…' : 'Save signature'}
        </button>
      </div>
    </div>
  )
}
