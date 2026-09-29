import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// jsdom has no object URLs; pages show API-served images (the signature) through them.
URL.createObjectURL ??= () => 'blob:test-object-url'
URL.revokeObjectURL ??= () => {}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})
