import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import { resetSession } from './utils'

// Pages show API-served files (the signature, report PDFs) through object URLs. jsdom has none, and where Node's
// own leak through they are random, so tests always get one fixed URL.
URL.createObjectURL = () => 'blob:test-object-url'
URL.revokeObjectURL = () => {}

// Every signed-in page first restores the session (POST /auth/refresh) and only then loads its data, so under a
// fully parallel run the default one-second wait for findBy* is too tight.
configure({ asyncUtilTimeout: 3000 })

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
  resetSession()
})
