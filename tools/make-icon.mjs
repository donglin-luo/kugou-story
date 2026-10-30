// A tiny original pixel/vector icon, generated without any image tools or Python.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const size = 32;
const pixels = Buffer.alloc(size * size * 4);
for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
  const rounded = x >= 6 && x <= 25 || y >= 6 && y <= 25 ||
    Math.hypot(x - (x < 16 ? 6 : 25), y - (y < 16 ? 6 : 25)) <= 6;
  const stem = x >= 18 && x <= 21 && y >= 7 && y <= 22;
  const beam = x >= 10 && x <= 21 && y >= 7 && y <= 10;
  const leftStem = x >= 10 && x <= 12 && y >= 9 && y <= 24;
  const note = ((x - 8.5) / 4) ** 2 + ((y - 24) / 3) ** 2 <= 1 ||
    ((x - 17) / 4) ** 2 + ((y - 22) / 3) ** 2 <= 1;
  const color = stem || beam || leftStem || note ? [109, 229, 205] : [20, 31, 49];
  const i = ((size - 1 - y) * size + x) * 4;
  pixels[i] = color[2]; pixels[i + 1] = color[1]; pixels[i + 2] = color[0]; pixels[i + 3] = rounded ? 255 : 0;
}
const dib = Buffer.alloc(40);
dib.writeUInt32LE(40, 0); dib.writeInt32LE(size, 4); dib.writeInt32LE(size * 2, 8);
dib.writeUInt16LE(1, 12); dib.writeUInt16LE(32, 14); dib.writeUInt32LE(pixels.length, 20);
const mask = Buffer.alloc(size / 8 * size);
const entry = Buffer.alloc(22); entry.writeUInt16LE(1, 2); entry.writeUInt16LE(1, 4);
entry[6] = size; entry[7] = size; entry.writeUInt16LE(1, 10); entry.writeUInt16LE(32, 12);
entry.writeUInt32LE(dib.length + pixels.length + mask.length, 14); entry.writeUInt32LE(22, 18);
const target = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src/KugouStory/app.ico');
fs.writeFileSync(target, Buffer.concat([entry, dib, pixels, mask]));
console.log('Created app.ico');
