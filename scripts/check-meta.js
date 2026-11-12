/**
 * Unity Meta File Validator
 * Ensures all staged files in Assets/ have corresponding .meta files and vice versa.
 */
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

function getStagedFiles() {
  try {
    const output = execSync('git diff --cached --name-only --diff-filter=ACM', { encoding: 'utf8' });
    return output.split(/\r?\n/).filter(Boolean);
  } catch (err) {
    console.error('Failed to get staged git files:', err.message);
    return [];
  }
}

function checkMetaFiles() {
  const staged = getStagedFiles();
  const assetsStaged = staged.filter(f => f.startsWith('Assets/'));
  
  if (assetsStaged.length === 0) {
    console.log('✓ No assets staged to check for .meta files.');
    return 0;
  }

  let errors = 0;
  const stagedSet = new Set(staged);

  for (const file of assetsStaged) {
    if (file.endsWith('.meta')) {
      // It's a meta file. Check if the asset exists
      const targetAsset = file.slice(0, -5);
      if (!fs.existsSync(targetAsset) && !stagedSet.has(targetAsset)) {
        console.warn(`⚠ Warning: Staged meta file for non-existent asset: ${file}`);
      }
    } else {
      // It's an asset. Check if .meta file exists
      const metaFile = `${file}.meta`;
      if (!fs.existsSync(metaFile)) {
        console.error(`✖ ERROR: Missing .meta file for asset: ${file}`);
        errors++;
      } else if (!stagedSet.has(metaFile)) {
        console.warn(`⚠ Warning: Asset staged without its .meta file: ${file} (meta exists on disk, consider staging ${metaFile})`);
      }
    }
  }

  if (errors > 0) {
    console.error(`\n✖ Meta file check failed with ${errors} error(s). Every file in Assets/ requires a .meta file in Unity.`);
    return 1;
  }

  console.log('✓ All staged assets have valid .meta files.');
  return 0;
}

process.exit(checkMetaFiles());

