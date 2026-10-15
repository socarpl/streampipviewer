const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const publish = require('../.github/scripts/release.cjs');

function release(tag, extra = {}) {
  return { id: tag, tag_name: tag, draft: false, prerelease: false,
    html_url: `https://example.test/releases/${tag}`, ...extra };
}

function fixture(initial = [], options = {}) {
  const state = { releases: initial, events: [], latest: null };
  const assets = new Map();
  const repos = {
    listReleases: () => {},
    listReleaseAssets: () => {},
    async createRelease(params) {
      assert.equal(params.target_commitish, 'test-commit');
      assert.equal(params.draft, true);
      const item = release(params.tag_name, { draft: true });
      state.releases.push(item);
      state.events.push('create');
      return { data: item };
    },
    async uploadReleaseAsset(params) {
      state.events.push('upload');
      if (options.failUpload) throw new Error('Upload failed');
      assert.ok(params.data.length > 0);
      assets.set(params.release_id, [{ id: 'zip', name: params.name, size: params.data.length, state: 'uploaded' }]);
    },
    async deleteReleaseAsset(params) {
      state.events.push('delete-asset');
      for (const [id, values] of assets) assets.set(id, values.filter(a => a.id !== params.asset_id));
    },
    async updateRelease(params) {
      state.events.push('publish');
      const item = state.releases.find(r => r.id === params.release_id);
      Object.assign(item, { draft: params.draft, prerelease: params.prerelease });
      if (params.make_latest === 'true') state.latest = item.tag_name;
      return { data: item };
    },
    async deleteRelease(params) {
      state.events.push(`delete:${params.release_id}`);
      state.releases = state.releases.filter(r => r.id !== params.release_id);
    }
  };
  const github = {
    rest: { repos },
    async paginate(method, params) {
      if (method === repos.listReleases) return state.releases.map(r => ({ ...r }));
      if (method === repos.listReleaseAssets) return assets.get(params.release_id) || [];
      throw new Error('Unexpected paginated endpoint');
    }
  };
  const summary = {
    addHeading() { return this; }, addLink() { return this; }, addRaw() { return this; }, async write() {}
  };
  return { state, assets, github, context: { repo: { owner: 'test', repo: 'viewer' }, sha: 'test-commit' },
    core: { info() {}, summary } };
}

const cases = [
  ['publishes matching version and keeps newest plus two previous releases', async env => {
    const f = fixture(['v0.5', 'v1.0.1.2', 'v1.0.1.9'].map(tag => release(tag)));
    await publish({ ...f, env });
    assert.equal(f.state.latest, 'v1.0.1.10');
    assert.deepEqual(f.state.releases.map(r => r.tag_name).sort(), ['v1.0.1.10', 'v1.0.1.2', 'v1.0.1.9']);
    assert.ok(f.state.events.indexOf('publish') < f.state.events.indexOf('delete:v0.5'));
  }],
  ['failed upload preserves all previous releases', async env => {
    const f = fixture(['v0.5', 'v1.0.1.2', 'v1.0.1.9'].map(tag => release(tag)), { failUpload: true });
    await assert.rejects(publish({ ...f, env }), /Upload failed/);
    assert.ok(!f.state.events.some(event => event.startsWith('delete:') || event === 'publish'));
  }],
  ['retry of published release leaves its download unchanged', async env => {
    const f = fixture([release('v1.0.1.10')]);
    f.assets.set('v1.0.1.10', [{ name: path.basename(env.ZIP_PATH), size: 100, state: 'uploaded' }]);
    await publish({ ...f, env });
    assert.deepEqual(f.state.events, []);
  }],
  ['published release missing a ZIP cannot trigger cleanup', async env => {
    const f = fixture(['v0.5', 'v1.0.1.2', 'v1.0.1.9', 'v1.0.1.10'].map(tag => release(tag)));
    await assert.rejects(publish({ ...f, env }), /missing its ZIP/);
    assert.deepEqual(f.state.events, []);
  }],
  ['retry resumes a draft and replaces only its incomplete asset', async env => {
    const f = fixture([release('v1.0.1.10', { draft: true })]);
    f.assets.set('v1.0.1.10', [{ id: 'incomplete', name: path.basename(env.ZIP_PATH), size: 0 }]);
    await publish({ ...f, env });
    assert.deepEqual(f.state.events, ['delete-asset', 'upload', 'publish']);
  }],
  ['older manual run never replaces latest or deletes newer releases', async env => {
    const f = fixture(['v1.0.1.11', 'v1.0.1.12', 'v1.0.1.13'].map(tag => release(tag)));
    await publish({ ...f, env });
    assert.equal(f.state.latest, null);
    assert.deepEqual(f.state.releases.map(r => r.tag_name), ['v1.0.1.11', 'v1.0.1.12', 'v1.0.1.13']);
  }],
  ['drafts, prereleases, and unrelated tags are preserved', async env => {
    const protectedReleases = [release('v0.1', { draft: true }), release('v0.2', { prerelease: true }), release('nightly')];
    const f = fixture([...protectedReleases, ...['v0.5', 'v1.0.1.2', 'v1.0.1.9'].map(tag => release(tag))]);
    await publish({ ...f, env });
    for (const item of protectedReleases) assert.ok(f.state.releases.some(r => r.id === item.id));
    assert.ok(!f.state.releases.some(r => r.tag_name === 'v0.5'));
  }],
  ['missing ZIP fails before any remote changes', async env => {
    const f = fixture();
    await assert.rejects(publish({ ...f, env: { ...env, ZIP_PATH: path.join('missing', path.basename(env.ZIP_PATH)) } }), /ENOENT/);
    assert.deepEqual(f.state.events, []);
  }]
];

(async () => {
  const tempDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'streampip-release-test-'));
  try {
    const zipPath = path.join(tempDirectory, 'StreamPIPViewer-v1.0.1.10-windows.zip');
    fs.writeFileSync(zipPath, Buffer.from('PK test ZIP data'));
    for (const [name, test] of cases) {
      await test({ APP_VERSION: '1.0.1.10', ZIP_PATH: zipPath });
      console.log(`PASS: ${name}`);
    }
    console.log(`All ${cases.length} release regression checks passed.`);
  } finally {
    fs.rmSync(tempDirectory, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
