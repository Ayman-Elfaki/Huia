import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import os from 'node:os'
import { huiaOidc } from '../src/vite.js'

describe('@huia/tanstack-start-oidc vite plugin', () => {
  it('validates session password length', () => {
    const plugin = huiaOidc()
    process.env.HUIA_SESSION_PASSWORD = 'too-short'
    expect(() => {
      // @ts-expect-error test hook call
      plugin.configResolved?.({ root: process.cwd() } as any)
    }).toThrow('HUIA_SESSION_PASSWORD must be at least 32 characters long')
    delete process.env.HUIA_SESSION_PASSWORD
  })

  it('scaffolds route file if missing and leaves existing file untouched', () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'huia-test-vite-'))
    const routeRel = path.join('routes', 'api', 'auth', '$.ts')
    const routeAbs = path.join(tmpDir, routeRel)

    const plugin = huiaOidc({
      routeFile: routeRel,
      authConfigImportPath: '@/auth.config',
    })

    // 1. Should create file when missing
    // @ts-expect-error test hook call
    plugin.configResolved?.({ root: tmpDir } as any)
    expect(fs.existsSync(routeAbs)).toBe(true)
    const content = fs.readFileSync(routeAbs, 'utf8')
    expect(content).toContain('@huia/tanstack-start-oidc')
    expect(content).toContain("createFileRoute('/api/auth/$')")

    // 2. Modify file and ensure it is NOT overwritten
    fs.writeFileSync(routeAbs, '// customized by user', 'utf8')
    // @ts-expect-error test hook call
    plugin.configResolved?.({ root: tmpDir } as any)
    expect(fs.readFileSync(routeAbs, 'utf8')).toBe('// customized by user')

    fs.rmSync(tmpDir, { recursive: true, force: true })
  })
})
