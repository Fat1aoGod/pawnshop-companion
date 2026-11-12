import test from 'node:test';
import assert from 'node:assert/strict';
import {pairingCode} from '../scripts/setup.mjs';
test('connection code carries only an allowed local port and a fresh secret',()=>{
  const token='a'.repeat(48);
  assert.equal(pairingCode({port:17643,token}),`PS1:17643:${token}`);
  for(const port of [0,80,65536,17643.1])assert.throws(()=>pairingCode({port,token}));
  for(const bad of ['', 'public-token', 'g'.repeat(48)])assert.throws(()=>pairingCode({port:17643,token:bad}));
});
