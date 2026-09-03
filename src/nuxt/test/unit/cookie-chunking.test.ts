import { describe, it, expect } from 'vitest'
import { sealValue, unsealValue, splitIntoChunks } from '../../src/runtime/server/utils/cookie'
import { testConfig } from '../support/config'
import type { CookiePayload } from '../../src/runtime/server/utils/internal-types'

const bigPayload: CookiePayload = {
  sid: 'b3f10c2e-0000-4000-8000-000000000000',
  user: {
    sub: 'user-1',
    name: 'Ada Lovelace',
    email: 'ada@example.test',
    // many roles → the sealed blob spans several chunks
    roles: Array.from({ length: 60 }, (_, i) => `role-number-${i}-with-a-longish-name`),
  },
  exp: Date.now() + 1_000_000,
}

describe('splitIntoChunks', () => {
  it('handles exact multiples, +1 and empty', () => {
    expect(splitIntoChunks('', 4)).toEqual([])
    expect(splitIntoChunks('abcd', 4)).toEqual(['abcd'])
    expect(splitIntoChunks('abcde', 4)).toEqual(['abcd', 'e'])
    expect(splitIntoChunks('abcdefgh', 4)).toEqual(['abcd', 'efgh'])
  })
})

describe('chunked reassembly', () => {
  it('splits a large sealed session and rejoins it', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 120, maxChunks: 16 } })
    const sealed = await sealValue(cfg, bigPayload)
    const parts = splitIntoChunks(sealed, cfg.cookie.chunkSize)
    expect(parts.length).toBeGreaterThan(1)
    expect(await unsealValue<CookiePayload>(cfg, parts.join(''))).toEqual(bigPayload)
  })

  it('a missing middle chunk yields null', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 120, maxChunks: 16 } })
    const parts = splitIntoChunks(await sealValue(cfg, bigPayload), 120)
    const gapped = [...parts.slice(0, 1), ...parts.slice(2)] // drop index 1
    expect(await unsealValue(cfg, gapped.join(''))).toBeNull()
  })

  it('a missing first chunk yields null', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 120, maxChunks: 16 } })
    const parts = splitIntoChunks(await sealValue(cfg, bigPayload), 120)
    expect(await unsealValue(cfg, parts.slice(1).join(''))).toBeNull()
  })

  it('reordered chunks yield null', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 120, maxChunks: 16 } })
    const parts = splitIntoChunks(await sealValue(cfg, bigPayload), 120)
    const swapped = [parts[1]!, parts[0]!, ...parts.slice(2)]
    expect(await unsealValue(cfg, swapped.join(''))).toBeNull()
  })

  it('a single tampered character yields null', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 120, maxChunks: 16 } })
    const sealed = await sealValue(cfg, bigPayload)
    const flipped = `${sealed.slice(0, 10)}${sealed[10] === 'a' ? 'b' : 'a'}${sealed.slice(11)}`
    expect(await unsealValue(cfg, flipped)).toBeNull()
  })

  it('enforces the maxChunks ceiling via splitIntoChunks length', async () => {
    const cfg = testConfig({ cookie: { chunkSize: 40, maxChunks: 3 } })
    const parts = splitIntoChunks(await sealValue(cfg, bigPayload), cfg.cookie.chunkSize)
    expect(parts.length).toBeGreaterThan(cfg.cookie.maxChunks)
  })
})
