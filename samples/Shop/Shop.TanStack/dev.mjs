import { createServer } from 'vite'
import { readFileSync, existsSync } from 'node:fs'

const port = parseInt(process.env.PORT || '3080', 10)
const certPath = process.env.TLS_CONFIG_CERT
const keyPath = process.env.TLS_CONFIG_KEY

let https = undefined
if (certPath && keyPath && existsSync(certPath) && existsSync(keyPath)) {
  https = {
    key: readFileSync(keyPath),
    cert: readFileSync(certPath),
    passphrase: process.env.TLS_CONFIG_PASSWORD,
  }
}

const server = await createServer({
  server: {
    port,
    host: true,
    https,
  },
})

await server.listen()
server.printUrls()
