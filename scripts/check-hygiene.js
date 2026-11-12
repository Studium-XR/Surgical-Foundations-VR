/**
 * Repository Hygiene Validator
 * Checks staged files for temporary, recovery, plastic SCM, or OS artifacts.
 */
const { execSync } = require('child_process');

const WARN_PATTERNS = [
  { pattern: /^\.plastic\//i, desc: 'Plastic SCM metadata file' },
  { pattern: /^Assets\/_Recovery\//i, desc: 'Unity crash recovery scene' },
  { pattern: /\.DS_Store$/i, desc: 'macOS metadata' },
  { pattern: /Thumbs\.db$/i, desc: 'Windows thumbnail cache' },
  { pattern: /\.orig$/i, desc: 'Merge artifact' },
  { pattern: /\.tmp$/i, desc: 'Temporary file' },
  { pattern: /^node_modules\//i, desc: 'Node dependencies (should be in .gitignore)' },
  { pattern: /\.apk$/i, desc: 'Android build binary' },
  { pattern: /\.aab$/i, desc: 'Android App Bundle' }
];

function getStagedFiles() {
  try {
    const output = execSync('git diff --cached --name-only --diff-filter=ACM', { encoding: 'utf8' });
    return output.split(/\r?\n/).filter(Boolean);
  } catch (err) {
    return [];
  }
}

function checkHygiene() {
  const staged = getStagedFiles();
  let warnings = 0;

  for (const file of staged) {
    for (const { pattern, desc } of WARN_PATTERNS) {
      if (pattern.test(file)) {
        console.warn(`⚠ Warning: Staged file matches ${desc}: "${file}". Recommend removing from git.`);
        warnings++;
        break;
      }
    }
  }

  if (warnings > 0) {
    console.log(`ℹ Found ${warnings} hygiene warning(s). Staging allowed, but clean-up recommended.`);
  } else {
    console.log('✓ Git hygiene check passed.');
  }
  return 0;
}

process.exit(checkHygiene());
