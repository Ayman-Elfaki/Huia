import { createServer, type Server } from 'node:http'
import { createHash, randomUUID } from 'node:crypto'
import { SignJWT, exportJWK, generateKeyPair, type KeyLike } from 'jose'

export interface MockOp {
  issuer: string
  origin: string
  close: () => Promise<void>
  parCalls: Array<Record<string, string>>
  tokenCalls: Array<Record<string, string>>
  lastAuthorizeUrl: string | null
}

const b64urlSha256 = (v: string) => createHash('sha256').update(v).digest('base64url')

interface Opts {
  tenant?: string
  advertisePar?: boolean
}

export async function startMockOp(opts: Opts = {}): Promise<MockOp> {
  const tenant = opts.tenant ?? 'acme'
  const advertisePar = opts.advertisePar ?? true
  const { publicKey, privateKey } = await generateKeyPair('RS256')
  const jwk = await exportJWK(publicKey as KeyLike)
  jwk.kid = 'mock-1'
  jwk.alg = 'RS256'
  jwk.use = 'sig'

  const parStore = new Map<string, Record<string, string>>()
  const codeStore = new Map<string, { code_challenge: string, nonce: string, redirect_uri: string }>()

  const state = {
    parCalls: [] as Array<Record<string, string>>,
    tokenCalls: [] as Array<Record<string, string>>,
    lastAuthorizeUrl: null as string | null,
  }

  let origin = ''
  const issuer = () => `${origin}/${tenant}`

  const readForm = (body: string): Record<string, string> =>
    Object.fromEntries(new URLSearchParams(body))

  const server: Server = createServer(async (req, res) => {
    const url = new URL(req.url ?? '/', `http://${req.headers.host}`)
    const path = url.pathname
    const chunks: Buffer[] = []
    for await (const c of req) chunks.push(c as Buffer)
    const rawBody = Buffer.concat(chunks).toString('utf8')

    const json = (code: number, obj: unknown) => {
      res.writeHead(code, { 'content-type': 'application/json' })
      res.end(JSON.stringify(obj))
    }

    if (path === `/${tenant}/.well-known/openid-configuration`) {
      return json(200, {
        issuer: issuer(),
        authorization_endpoint: `${issuer()}/connect/authorize`,
        token_endpoint: `${issuer()}/connect/token`,
        userinfo_endpoint: `${issuer()}/connect/userinfo`,
        jwks_uri: `${issuer()}/jwks`,
        end_session_endpoint: `${issuer()}/connect/logout`,
        ...(advertisePar ? { pushed_authorization_request_endpoint: `${issuer()}/connect/par` } : {}),
        response_types_supported: ['code'],
        grant_types_supported: ['authorization_code', 'refresh_token'],
        subject_types_supported: ['public'],
        id_token_signing_alg_values_supported: ['RS256'],
        code_challenge_methods_supported: ['S256'],
        token_endpoint_auth_methods_supported: ['client_secret_post'],
        authorization_response_iss_parameter_supported: true,
        scopes_supported: ['openid', 'profile', 'email', 'offline_access'],
      })
    }

    if (path === `/${tenant}/jwks`) {
      return json(200, { keys: [jwk] })
    }

    if (path === `/${tenant}/connect/par` && req.method === 'POST') {
      const form = readForm(rawBody)
      state.parCalls.push(form)
      const id = `urn:ietf:params:oauth:request_uri:${randomUUID()}`
      parStore.set(id, form)
      return json(201, { request_uri: id, expires_in: 90 })
    }

    if (path === `/${tenant}/connect/authorize`) {
      state.lastAuthorizeUrl = url.href
      const requestUri = url.searchParams.get('request_uri')
      const params = requestUri
        ? parStore.get(requestUri)
        : Object.fromEntries(url.searchParams)
      if (!params) {
        res.writeHead(400)
        return res.end('unknown request_uri')
      }
      const code = randomUUID()
      codeStore.set(code, {
        code_challenge: params.code_challenge ?? '',
        nonce: params.nonce ?? '',
        redirect_uri: params.redirect_uri ?? '',
      })
      const cb = new URL(params.redirect_uri ?? '')
      cb.searchParams.set('code', code)
      cb.searchParams.set('state', params.state ?? '')
      cb.searchParams.set('iss', issuer())
      res.writeHead(302, { location: cb.href })
      return res.end()
    }

    if (path === `/${tenant}/connect/token` && req.method === 'POST') {
      if (!(req.headers['content-type'] ?? '').includes('application/x-www-form-urlencoded')) {
        return json(415, { error: 'invalid_request', error_description: 'form-encoded only' })
      }
      const form = readForm(rawBody)
      state.tokenCalls.push(form)

      const mkTokens = async (nonce: string) => {
        // `role` (singular) — the claim key OpenIddict/Huia actually emit; pickUserClaims maps it to `roles`.
        const idToken = await new SignJWT({ nonce, name: 'Ada Lovelace', email: 'ada@example.test', role: ['admin', 'user'] })
          .setProtectedHeader({ alg: 'RS256', kid: 'mock-1' })
          .setIssuer(issuer())
          .setSubject('user-ada')
          .setAudience(form.client_id ?? 'acme-web')
          .setIssuedAt()
          .setExpirationTime('5m')
          .sign(privateKey)
        return {
          access_token: `at-${randomUUID()}`,
          refresh_token: `rt-${randomUUID()}`,
          id_token: idToken,
          token_type: 'Bearer',
          expires_in: 300,
          scope: form.scope ?? 'openid profile email offline_access',
        }
      }

      if (form.grant_type === 'authorization_code') {
        const rec = codeStore.get(form.code ?? '')
        if (!rec) return json(400, { error: 'invalid_grant' })
        if (b64urlSha256(form.code_verifier ?? '') !== rec.code_challenge) {
          return json(400, { error: 'invalid_grant', error_description: 'pkce' })
        }
        codeStore.delete(form.code ?? '')
        return json(200, await mkTokens(rec.nonce))
      }
      if (form.grant_type === 'refresh_token') {
        if (!form.refresh_token || form.refresh_token === 'revoked') {
          return json(400, { error: 'invalid_grant' })
        }
        return json(200, await mkTokens(''))
      }
      return json(400, { error: 'unsupported_grant_type' })
    }

    if (path === `/${tenant}/connect/userinfo`) {
      return json(200, { sub: 'user-ada', name: 'Ada Lovelace', email: 'ada@example.test' })
    }

    if (path === `/${tenant}/connect/logout`) {
      const to = url.searchParams.get('post_logout_redirect_uri') ?? '/'
      res.writeHead(302, { location: to })
      return res.end()
    }

    res.writeHead(404)
    res.end('not found')
  })

  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const addr = server.address()
  if (addr === null || typeof addr === 'string') throw new Error('mock OP failed to bind')
  origin = `http://127.0.0.1:${addr.port}`

  return {
    issuer: issuer(),
    origin,
    close: () => new Promise<void>(resolve => server.close(() => resolve())),
    get parCalls() { return state.parCalls },
    get tokenCalls() { return state.tokenCalls },
    get lastAuthorizeUrl() { return state.lastAuthorizeUrl },
  }
}
