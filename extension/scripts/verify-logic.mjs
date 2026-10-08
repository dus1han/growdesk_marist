// Checks the toolbar's pure logic: which fields show, what is still required, and the exact
// request GrowDesk receives. Bundles the real source modules with esbuild so the checked code
// is the same code Chrome runs. Run with: node scripts/verify-logic.mjs
import { build } from 'esbuild';
import assert from 'node:assert/strict';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const bundle = await build({
  entryPoints: [resolve(ROOT, 'scripts/logic-entry.ts')],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'neutral',
});

const {
  buildRequest,
  canSave,
  displayValue,
  enabledFields,
  fieldKind,
  missingRequired,
  saveBlocker,
  mergeHighlight,
  normalizeSelection,
  platformFromHostname,
  isNewerVersion,
  updateRequired,
  isConfigured,
  normalizeServer,
  originPattern,
} = await import('data:text/javascript;base64,' + Buffer.from(bundle.outputFiles[0].text).toString('base64'));

// A field setup like GrowDesk's default, plus two custom fields.
const f = (key, label, type, enabled, required, order, extra = {}) => ({ key, label, type, enabled, required, order, isCustom: false, options: null, ...extra });
const config = {
  server: 'http://169.58.92.105:3110',
  fetchedAt: '2026-09-30T10:00:00Z',
  fields: [
    f('whatsapp', 'WhatsApp Number', 'phone', true, true, 2),
    f('name', 'Name', 'text', true, true, 1),
    f('instagram', 'Instagram', 'text', true, false, 3),
    f('treatments', 'Interested Treatments', 'multiselect', true, false, 4),
    f('stage', 'Stage', 'dropdown', true, false, 5),
    f('lead_source', 'Lead Source', 'dropdown', true, false, 6),
    f('notes', 'Notes', 'textarea', true, false, 7),
    f('email', 'Email', 'email', false, false, 8),
    f('clinic', 'Preferred Clinic', 'dropdown', true, false, 9, { isCustom: true, options: [{ id: 4, label: 'Downtown' }, { id: 5, label: 'Marina' }] }),
    f('budget', 'Budget', 'number', true, false, 10, { isCustom: true }),
  ],
  treatments: [{ id: 1, name: 'Botox', color: null }, { id: 2, name: 'Filler', color: null }],
  stages: [{ id: 1, name: 'Interested', color: '#6366F1' }],
  sources: [{ id: 7, name: 'WhatsApp', color: null }],
};
const session = (values) => ({ active: true, source: 'WhatsApp', values });

const results = [];
const check = (name, fn) => {
  try {
    fn();
    results.push(['PASS', name]);
  } catch (error) {
    results.push(['FAIL', `${name} :: ${error.message}`]);
  }
};

check('Fields: only enabled ones, in the admin order; lead source is never asked (set from the site)', () => {
  assert.deepEqual(enabledFields(config).map((x) => x.key), ['name', 'whatsapp', 'instagram', 'treatments', 'stage', 'notes', 'clinic', 'budget']);
  assert.equal(fieldKind(config.fields[3]), 'multi');
  assert.equal(fieldKind(config.fields[4]), 'single');
  assert.equal(fieldKind(config.fields[0]), 'highlight');
});
check('Required: STOP blocked until every required field has a value', () => {
  assert.deepEqual(missingRequired(session({ name: 'Sarah' }), config).map((x) => x.key), ['whatsapp']);
  assert.equal(saveBlocker(session({ name: 'Sarah' }), config), 'Still needed: WhatsApp Number');
  assert.equal(canSave(session({ name: 'Sarah', whatsapp: '050 123 4567' }), config), true);
});
check('Required fields follow the admin: a newly required field blocks STOP', () => {
  const stricter = { ...config, fields: config.fields.map((x) => (x.key === 'treatments' ? { ...x, required: true } : x)) };
  assert.equal(canSave(session({ name: 'Sarah', whatsapp: '050 123 4567' }), stricter), false);
  assert.equal(canSave(session({ name: 'Sarah', whatsapp: '050 123 4567', treatments: [1] }), stricter), true);
});
check('Identifier: WhatsApp or Instagram needed even when neither is required', () => {
  const relaxed = { ...config, fields: config.fields.map((x) => ({ ...x, required: x.key === 'name' })) };
  assert.equal(saveBlocker(session({ name: 'Sarah' }), relaxed), 'Capture a WhatsApp number or an Instagram name.');
  assert.equal(canSave(session({ name: 'Sarah', instagram: '@sarah' }), relaxed), true);
});
check('Not connected or not started: STOP blocked', () => {
  assert.equal(canSave(session({ name: 'x', whatsapp: '1' }), null), false);
  assert.equal(canSave(null, config), false);
});
check('Request: built-in keys map to the API names; disabled fields are left out; the site is the source', () => {
  const body = buildRequest(
    session({ name: ' Sarah ', whatsapp: '050 123 4567', treatments: [1, 2], stage: 1, lead_source: 7, notes: 'Asked price', email: 'x@y.z', clinic: 5, budget: '1,500' }),
    config,
  );
  assert.deepEqual(body, {
    name: 'Sarah',
    whatsApp: '050 123 4567',
    treatmentIds: [1, 2],
    stageId: 1,
    notes: 'Asked price',
    customFields: { clinic: 5, budget: 1500 },
    source: 'whatsapp',
  });
});
check('Request: a capture on Instagram says so', () => {
  const s = { ...session({ instagram: '@sarah' }), source: 'Instagram' };
  assert.equal(buildRequest(s, config).source, 'instagram');
});
check('Required lead source no longer blocks STOP (GrowDesk sets it)', () => {
  const strict = { ...config, fields: config.fields.map((x) => (x.key === 'lead_source' ? { ...x, required: true } : x)) };
  assert.equal(canSave(session({ name: 'Sarah', whatsapp: '050 123 4567' }), strict), true);
});
check('Display: list values show their names', () => {
  assert.equal(displayValue(config.fields[3], [1, 2], config), 'Botox, Filler');
  assert.equal(displayValue(config.fields[8], 5, config), 'Marina');
});
check('Highlights: Instagram URL becomes a handle; notes collect, others replace', () => {
  assert.equal(normalizeSelection({ key: 'instagram', type: 'text' }, 'https://www.instagram.com/sarah.f/'), '@sarah.f');
  assert.equal(normalizeSelection({ key: 'name', type: 'text' }, '  Sarah\n Fernando '), 'Sarah Fernando');
  assert.equal(mergeHighlight({ type: 'textarea' }, 'Asked price', 'Prefers mornings'), 'Asked price\nPrefers mornings');
  assert.equal(mergeHighlight({ type: 'text' }, 'Old', 'New'), 'New');
});
check('Server address: bare IP means http, a name means https', () => {
  assert.equal(normalizeServer('169.58.92.105:3110'), 'http://169.58.92.105:3110');
  assert.equal(normalizeServer('crm.example.com/'), 'https://crm.example.com');
  assert.equal(normalizeServer('http://169.58.92.105:3110/login'), 'http://169.58.92.105:3110');
  assert.equal(normalizeServer('ftp://x'), null);
  assert.equal(originPattern('http://169.58.92.105:3110'), 'http://169.58.92.105:3110/*');
  assert.equal(isConfigured({ serverUrl: '169.58.92.105:3110', clientId: 'gdc_1', clientSecret: 'gds_2' }), true);
  assert.equal(isConfigured({ serverUrl: '169.58.92.105:3110', clientId: '', clientSecret: 'gds_2' }), false);
});
check('Update notice: version comparison', () => {
  assert.equal(isNewerVersion('1.0.8', '1.0.7'), true);
  assert.equal(isNewerVersion('1.0.10', '1.0.9'), true);
  assert.equal(isNewerVersion('1.1', '1.0.9'), true);
  assert.equal(isNewerVersion('1.0.7', '1.0.7'), false);
  assert.equal(isNewerVersion('1.0.6', '1.0.7'), false);
  assert.equal(updateRequired({ latest: { version: '1.0.10', download: '', guide: '' } }, '1.0.9'), true);
  assert.equal(updateRequired({ latest: { version: '1.0.9', download: '', guide: '' } }, '1.0.9'), false);
  assert.equal(updateRequired({ latest: null }, '1.0.9'), false);
  assert.equal(updateRequired(null, '1.0.9'), false);
});
check('Platform detection', () => {
  assert.equal(platformFromHostname('web.whatsapp.com'), 'WhatsApp');
  assert.equal(platformFromHostname('www.instagram.com'), 'Instagram');
  assert.equal(platformFromHostname('example.com'), null);
});

for (const [status, name] of results) console.log(`${status}  ${name}`);
const failed = results.filter(([s]) => s === 'FAIL').length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
