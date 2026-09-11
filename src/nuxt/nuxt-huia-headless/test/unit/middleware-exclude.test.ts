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
    expect(matchesPattern('/auth/login', '/auth/**')).toBe(true)
    expect(matchesPattern('/authorization', '/auth/**')).toBe(false)
  })
})

describe('isExcluded', () => {
  it('is true when any pattern in the list matches', () => {
    const exclude = ['/', '/login', '/auth/**']
    expect(isExcluded('/', exclude)).toBe(true)
    expect(isExcluded('/login', exclude)).toBe(true)
    expect(isExcluded('/auth/login', exclude)).toBe(true)
    expect(isExcluded('/cart', exclude)).toBe(false)
  })

  it('is false for an empty exclude list', () => {
    expect(isExcluded('/anything', [])).toBe(false)
  })
})
