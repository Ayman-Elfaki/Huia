import { describe, it, expect } from 'vitest'
import { matchesPattern, isExcluded } from '../../src/runtime/app/utils/route-match'

describe('matchesPattern', () => {
  it('matches an exact literal path', () => {
    expect(matchesPattern('/about', '/about')).toBe(true)
    expect(matchesPattern('/about-us', '/about')).toBe(false)
  })

  it('matches a single segment with *', () => {
    expect(matchesPattern('/blog/hello-world', '/blog/*')).toBe(true)
    expect(matchesPattern('/blog/hello/world', '/blog/*')).toBe(false)
  })

  it('matches any depth under a prefix with /**', () => {
    expect(matchesPattern('/auth', '/auth/**')).toBe(true)
    expect(matchesPattern('/auth/oidc/login', '/auth/**')).toBe(true)
    expect(matchesPattern('/authorization', '/auth/**')).toBe(false)
  })

  it('does not treat regex metacharacters in the path as special', () => {
    expect(matchesPattern('/a.b', '/a.b')).toBe(true)
    expect(matchesPattern('/axb', '/a.b')).toBe(false)
  })
})

describe('isExcluded', () => {
  it('is true when any pattern in the list matches', () => {
    const exclude = ['/', '/about', '/auth/**']
    expect(isExcluded('/', exclude)).toBe(true)
    expect(isExcluded('/about', exclude)).toBe(true)
    expect(isExcluded('/auth/oidc/login', exclude)).toBe(true)
    expect(isExcluded('/protected', exclude)).toBe(false)
  })

  it('is false for an empty exclude list', () => {
    expect(isExcluded('/anything', [])).toBe(false)
  })
})
