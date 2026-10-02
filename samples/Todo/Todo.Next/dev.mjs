import { createServer } from 'node:https'
import { createServer as createHttpServer } from 'node:http'
import { readFileSync, existsSync } from 'node:fs'
import next from 'next'

const port = parseInt(process.env.PORT || '3050', 10)
const dev = process.env.NODE_ENV !== 'production'
const app = next({ dev, port })
const handle = app.getRequestHandler()

await app.prepare()

const certPath = process.env.TLS_CONFIG_CERT
const keyPath = process.env.TLS_CONFIG_KEY

if (certPath && keyPath && existsSync(certPath) && existsSync(keyPath)) {
  const httpsOptions = {
    key: readFileSync(keyPath),
    cert: readFileSync(certPath),
    passphrase: process.env.TLS_CONFIG_PASSWORD,
  }
  createServer(httpsOptions, (req, res) => handle(req, res)).listen(port, () => {
    console.log(`> Ready on https://localhost:${port}`)
  })
} else {
  createHttpServer((req, res) => handle(req, res)).listen(port, () => {
    console.log(`> Ready on http://localhost:${port}`)
  })
}
