import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const mode = args[0] === 'watch' ? 'watch' : 'run';
const filteredArgs = args
  .filter((arg) => arg !== 'run' && arg !== 'watch')
  .filter((arg) => arg !== '--runInBand' && arg !== '-i');

const packageDir = process.cwd();
const candidates = [
  path.join(packageDir, 'node_modules', 'vitest', 'vitest.mjs'),
  path.join(packageDir, 'node_modules', '.bin', process.platform === 'win32' ? 'vitest.cmd' : 'vitest'),
];

const vitestEntry = candidates.find((candidate) => existsSync(candidate));

if (!vitestEntry) {
  console.error('Vitest binary not found in this package. Run npm install first.');
  process.exit(1);
}

const nodeBin = process.execPath;
const result = spawnSync(nodeBin, [vitestEntry, mode, ...filteredArgs], {
  stdio: 'inherit',
  env: process.env,
});

if (result.error) {
  console.error(result.error);
  process.exit(1);
}

process.exit(result.status ?? 0);
