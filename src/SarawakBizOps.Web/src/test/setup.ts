import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// Pages show API-served files (the signature, report PDFs) through object URLs. jsdom has none, and where Node's
// own leak through they are random, so tests always get one fixed URL.
URL.createObjectURL = () => 'blob:test-object-url'
URL.revokeObjectURL = () => {}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})
