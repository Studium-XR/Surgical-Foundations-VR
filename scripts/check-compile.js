/**
 * C# Compilation Validator
 * Builds SurgicalFoundations.Runtime.csproj and SurgicalFoundations.Tests.EditMode.csproj
 * to ensure no broken code gets pushed.
 */
const { spawnSync } = require('child_process');
const fs = require('fs');

const projects = [
  'SurgicalFoundations.Runtime.csproj',
  'SurgicalFoundations.Tests.EditMode.csproj'
];

function checkCompile() {
  console.log('Building C# projects before push...');
  let failed = false;

  for (const proj of projects) {
    if (!fs.existsSync(proj)) {
      console.warn(`Project file ${proj} not found, skipping.`);
      continue;
    }

    console.log(`Checking ${proj}...`);
    const result = spawnSync('dotnet', ['build', proj, '-nologo', '-v', 'q'], {
      stdio: 'inherit',
      shell: true
    });

    if (result.status !== 0) {
      console.error(`✖ Compilation failed for ${proj}`);
      failed = true;
    } else {
      console.log(`✓ ${proj} compiled successfully.`);
    }
  }

  if (failed) {
    console.error('\n✖ Pre-push build check FAILED. Fix compilation errors before pushing.');
    return 1;
  }

  console.log('✓ All projects compiled cleanly. Ready to push.');
  return 0;
}

process.exit(checkCompile());

