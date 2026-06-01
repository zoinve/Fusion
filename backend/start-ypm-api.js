const fs = require('fs')
const path = require('path')
const os = require('os')

async function start() {
  const apiRoot = path.resolve(
    __dirname,
    'node_modules',
    '@neteasecloudmusicapienhanced',
    'api',
  )
  const tokenPath = path.resolve(os.tmpdir(), 'anonymous_token')

  if (!fs.existsSync(tokenPath)) {
    fs.writeFileSync(tokenPath, '', 'utf-8')
  }

  const generateConfig = require(path.join(apiRoot, 'generateConfig'))
  await generateConfig()

  const { serveNcmApi } = require(path.join(apiRoot, 'server'))
  await serveNcmApi({
    host: process.env.HOST || '127.0.0.1',
    port: Number(process.env.PORT || '3000'),
    checkVersion: false,
  })
}

start().catch((error) => {
  console.error(error)
  process.exit(1)
})
