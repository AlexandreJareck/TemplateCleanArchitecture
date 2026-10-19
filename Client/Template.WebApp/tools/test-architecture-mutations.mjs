import { rmSync, writeFileSync } from 'node:fs';
import { resolve, sep } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const pnpmCli = process.env['npm_execpath'];
if (!pnpmCli) throw new Error('Run this check through pnpm.');
const pnpmIsScript = /\.(?:c?js)$/i.test(pnpmCli);
const pnpmExecutable = pnpmIsScript ? process.execPath : pnpmCli;
const pnpmArguments = pnpmIsScript ? [pnpmCli] : [];
const cases = [
  ['src/app/domain/__mutation__.ts', "import '@angular/core';\n", 'domain-is-framework-free'],
  [
    'src/app/application/__mutation__.ts',
    "import '../infrastructure/runtime/runtime-config';\n",
    'application-does-not-depend-outward',
  ],
  [
    'src/app/infrastructure/__mutation__.ts',
    "import '../presentation/routes';\n",
    'infrastructure-does-not-depend-on-ui-or-bootstrap',
  ],
  [
    'src/app/presentation/__mutation__.ts',
    "import '../infrastructure/api/generated/models/departmentView';\n",
    'presentation-does-not-depend-on-infrastructure-or-bootstrap',
  ],
  [
    'src/app/domain/__mutation-bootstrap__.ts',
    "import '../bootstrap/tokens';\n",
    'bootstrap-is-not-imported-by-layers',
  ],
];

const mutationFiles = cases.map(([path]) => resolve(root, path));
for (const file of mutationFiles) {
  if (!file.startsWith(resolve(root, 'src/app') + sep)) throw new Error('Unsafe mutation path.');
}

try {
  for (const [path, source] of cases) {
    const file = resolve(root, path);
    writeFileSync(file, source, 'utf8');
  }
  const result = spawnSync(
    pnpmExecutable,
    [...pnpmArguments, 'exec', 'depcruise', '--config', 'dependency-cruiser.config.cjs', 'src/app'],
    {
      cwd: root,
      encoding: 'utf8',
    },
  );
  const output = `${result.stdout ?? ''}${result.stderr ?? ''}`;
  for (const [, , expectedRule] of cases) {
    if (result.status === 0 || !output.includes(expectedRule)) {
      throw new Error(`Architecture mutations did not trigger ${expectedRule}.\n${output}`);
    }
  }
  console.log(`${cases.length} architecture mutations rejected.`);
} finally {
  for (const file of mutationFiles) rmSync(file, { force: true });
}
