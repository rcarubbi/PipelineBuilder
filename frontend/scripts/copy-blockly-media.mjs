import { cpSync } from 'node:fs';

cpSync(new URL('../node_modules/blockly/media/', import.meta.url), new URL('../public/blockly/', import.meta.url), { recursive: true });
