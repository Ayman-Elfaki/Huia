import { preview } from 'vite'
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

const previewServer = await preview({
  preview: {
    port,
    host: '0.0.0.0',
    https,
  },
})

previewServer.printUrls()
