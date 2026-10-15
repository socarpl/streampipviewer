const fs = require('node:fs');
const path = require('node:path');

function versionParts(tag) {
  return /^v\d+\.\d+(?:\.\d+){0,2}$/.test(tag)
    ? tag.slice(1).split('.').map(Number).concat([0, 0]).slice(0, 4)
    : null;
}

function newestFirst(a, b) {
  const left = versionParts(a.tag_name);
  const right = versionParts(b.tag_name);
  for (let i = 0; i < 4; i++) {
    if (left[i] !== right[i]) return right[i] - left[i];
  }
  return 0;
}

module.exports = async function publish({ github, context, core, env = process.env }) {
  if (!/^\d+\.\d+\.\d+\.\d+$/.test(env.APP_VERSION)) {
    throw new Error('APP_VERSION must have four numeric components.');
  }
  const zipPath = env.ZIP_PATH;
  const fileName = `StreamPIPViewer-v${env.APP_VERSION}-windows.zip`;
  if (!zipPath || path.basename(zipPath) !== fileName) {
    throw new Error('ZIP_PATH must match the application version.');
  }
  // Validate/read the ZIP before making any remote changes.
  const data = fs.readFileSync(zipPath);
  if (!data.length) throw new Error('Release ZIP is empty.');
  const repo = context.repo;
  const tag = `v${env.APP_VERSION}`;
  const releases = await github.paginate(github.rest.repos.listReleases, { ...repo, per_page: 100 });
  let release = releases.find(item => item.tag_name === tag);
  if (release && !release.draft) {
    // A retry must never replace or remove an already published download.
    const assets = await github.paginate(github.rest.repos.listReleaseAssets,
      { ...repo, release_id: release.id, per_page: 100 });
    if (!assets.some(asset => asset.name === fileName && asset.state === 'uploaded' && asset.size > 0)) {
      throw new Error(`Published release ${tag} is missing its ZIP; refusing to prune older releases.`);
    }
    core.info(`${tag} is already published; retaining its existing download.`);
  } else {
    if (!release) {
      ({ data: release } = await github.rest.repos.createRelease({
        ...repo, tag_name: tag, target_commitish: context.sha,
        name: `Stream PIP Viewer ${env.APP_VERSION}`, draft: true,
        generate_release_notes: true
      }));
    }
    const assets = await github.paginate(github.rest.repos.listReleaseAssets,
      { ...repo, release_id: release.id, per_page: 100 });
    for (const asset of assets.filter(item => item.name === fileName)) {
      await github.rest.repos.deleteReleaseAsset({ ...repo, asset_id: asset.id });
    }
    await github.rest.repos.uploadReleaseAsset({
      ...repo, release_id: release.id, name: fileName, data,
      headers: { 'content-type': 'application/zip', 'content-length': data.length }
    });
    const newerReleaseExists = releases.some(item => !item.draft && !item.prerelease &&
      versionParts(item.tag_name) && newestFirst(item, { tag_name: tag }) < 0);
    ({ data: release } = await github.rest.repos.updateRelease({
      ...repo, release_id: release.id, draft: false, prerelease: false,
      make_latest: newerReleaseExists ? 'false' : 'true'
    }));
    core.info(`Published ${release.html_url}`);
  }

  // Only prune after publication succeeds. Keep tags as permanent source references.
  const published = (await github.paginate(github.rest.repos.listReleases, { ...repo, per_page: 100 }))
    .filter(item => !item.draft && !item.prerelease && versionParts(item.tag_name))
    .sort(newestFirst);
  for (const oldRelease of published.slice(3)) {
    core.info(`Deleting old release and download: ${oldRelease.tag_name}`);
    await github.rest.repos.deleteRelease({ ...repo, release_id: oldRelease.id });
  }
  await core.summary.addHeading(`Released ${tag}`)
    .addLink('Download release', release.html_url)
    .addRaw('\n\nRetention: latest release plus two previous releases. Git tags are preserved.')
    .write();
};
