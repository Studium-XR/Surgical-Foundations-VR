/**
 * File Size & Git LFS Validator
 * Prevents committing files over GitHub's 100MB limit and warns on files > 50MB.
 */
const { execSync } = require('child_process');
const fs = require('fs');

const GITHUB_MAX_BYTES = 100 * 1024 * 1024; // 100 MB hard limit
const WARNING_BYTES = 50 * 1024 * 1024;    // 50 MB warning threshold

function getStagedFiles() {
  try {
    const output = execSync('git diff --cached --name-only --diff-filter=ACM', { encoding: 'utf8' });
    return output.split(/\r?\n/).filter(Boolean);
  } catch (err) {
    return [];
  }
}

function checkFilesizes() {
  const staged = getStagedFiles();
  let fatalErrors = 0;

  for (const file of staged) {
    if (!fs.existsSync(file)) continue;
    try {
      const stats = fs.statSync(file);
      if (stats.isDirectory()) continue;

      const sizeMB = (stats.size / (1024 * 1024)).toFixed(2);

      if (stats.size > GITHUB_MAX_BYTES) {
        console.error(`✖ ERROR: "${file}" is ${sizeMB} MB, exceeding GitHub's 100MB limit. Push will be rejected! Use Git LFS.`);
        fatalErrors++;
      } else if (stats.size > WARNING_BYTES) {
        console.warn(`⚠ Warning: "${file}" is ${sizeMB} MB. Consider tracking with Git LFS.`);
      }
    } catch (e) {
      // Ignore files that cannot be stat'd
    }
  }

  if (fatalErrors > 0) {
    console.error(`\n✖ File size validation failed with ${fatalErrors} oversized file(s).`);
    return 1;
  }

  console.log('✓ File size check passed (no files > 100MB).');
  return 0;
}

process.exit(checkFilesizes());

