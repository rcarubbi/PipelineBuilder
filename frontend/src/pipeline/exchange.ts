import * as Blockly from 'blockly/core';
import { compileWorkspace, definitionToWorkspace } from '../blockly/workspace';
import type { Column, Definition, PipelineDocument, SavedPipeline, Step } from './types';

const maxImportBytes = 1024 * 1024;
const isRecord = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

function normalizedColumn(value: unknown): Column {
  if (!isRecord(value)) throw new Error('Definition contains an invalid column.');
  return { ...value, nullable: value.nullable ?? false, precision: value.precision ?? 18, scale: value.scale ?? 2 } as Column;
}

function normalizeDefinition(value: unknown): Definition {
  if (!isRecord(value) || value.formatVersion !== 1 || !Array.isArray(value.steps))
    throw new Error('Definition must use format version 1 and contain steps.');
  const steps = value.steps.map((raw): Step => {
    if (!isRecord(raw) || typeof raw.id !== 'string') throw new Error('Definition contains an invalid step.');
    if (raw.type === 'csv' && Array.isArray(raw.schema))
      return { ...raw, delimiter: raw.delimiter ?? ',', hasHeader: raw.hasHeader ?? true, schema: raw.schema.map(normalizedColumn) } as Step;
    if ((raw.type === 'parquetInput' || raw.type === 'excelInput') && Array.isArray(raw.schema))
      return { ...raw, ...(raw.type === 'excelInput' ? { hasHeader: raw.hasHeader ?? true } : {}), schema: raw.schema.map(normalizedColumn) } as Step;
    if (raw.type === 'map' && Array.isArray(raw.mappings))
      return { ...raw, mappings: raw.mappings.map(mapping => {
        if (!isRecord(mapping)) throw new Error('Definition contains an invalid mapping.');
        return { ...mapping, target: normalizedColumn(mapping.target), format: mapping.format ?? null };
      }) } as Step;
    if (raw.type === 'filter' && isRecord(raw.criteria)) return raw as unknown as Step;
    if (raw.type === 'parquet') return raw as unknown as Step;
    if (raw.type === 'csvOutput') return { ...raw, delimiter: raw.delimiter ?? ',', hasHeader: raw.hasHeader ?? true } as Step;
    if (raw.type === 'excelOutput') return { ...raw, hasHeader: raw.hasHeader ?? true } as Step;
    throw new Error(`Definition contains an invalid '${String(raw.type)}' step.`);
  });
  return { formatVersion: 1, steps, variables: Array.isArray(value.variables) ? value.variables as Definition['variables'] : [] };
}

function stable(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stable).join(',')}]`;
  if (isRecord(value)) return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${stable(value[key])}`).join(',')}}`;
  return JSON.stringify(value);
}

function definitionWorkspace(definition: Definition): Record<string, unknown> {
  const workspace = new Blockly.Workspace();
  try {
    Blockly.serialization.workspaces.load(definitionToWorkspace(definition), workspace);
    const compiled = normalizeDefinition(compileWorkspace(workspace));
    if (stable(compiled) !== stable(definition)) throw new Error('Blockly workspace does not match the executable definition.');
    return Blockly.serialization.workspaces.save(workspace) as Record<string, unknown>;
  } finally { workspace.dispose(); }
}

function fileNameWithoutExtension(fileName: string): string {
  const base = fileName.split(/[\\/]/).at(-1)?.replace(/\.json$/i, '').trim();
  return base || 'Imported pipeline';
}

function referencedVariables(definition: Definition): string[] {
  const variables = new Set<string>();
  for (const step of definition.steps) {
    if (!('path' in step)) continue;
    for (const match of step.path.matchAll(/\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g)) variables.add(match[1]);
  }
  return [...variables].sort();
}

export function parseImport(text: string, fileName: string): PipelineDocument {
  if (new TextEncoder().encode(text).byteLength > maxImportBytes) throw new Error('JSON files must be 1 MiB or smaller.');
  let parsed: unknown;
  try { parsed = JSON.parse(text); } catch { throw new Error('The selected file is not valid JSON.'); }
  if (!isRecord(parsed)) throw new Error('JSON must contain a pipeline export or a definition.');

  if (parsed.format === 'pipeline-builder/pipeline') {
    if (parsed.formatVersion !== 1 || !isRecord(parsed.pipeline)) throw new Error('Unsupported pipeline export format or version.');
    const source = parsed.pipeline;
    if (typeof source.name !== 'string' || !isRecord(source.environments)) throw new Error('Pipeline export is missing its name or environments.');
    const definition = normalizeDefinition(source.definition);
    const workspace = source.workspace === undefined ? definitionWorkspace(definition) : (() => {
      if (!isRecord(source.workspace)) throw new Error('Pipeline workspace must be a JSON object.');
      const candidate = new Blockly.Workspace();
      try {
        Blockly.serialization.workspaces.load(source.workspace, candidate);
        const compiled = normalizeDefinition(compileWorkspace(candidate));
        if (stable(compiled) !== stable(definition)) throw new Error('Blockly workspace does not match the executable definition.');
      } catch (error) {
        if (error instanceof Error && error.message === 'Blockly workspace does not match the executable definition.') throw error;
        throw new Error(`Could not restore Blockly workspace: ${error instanceof Error ? error.message : 'invalid workspace'}`);
      } finally { candidate.dispose(); }
      return source.workspace as Record<string, unknown>;
    })();
    return { name: source.name, definition, workspace, environments: source.environments as PipelineDocument['environments'] };
  }

  if (parsed.format !== undefined) throw new Error('Unsupported pipeline export format.');
  const definition = normalizeDefinition(parsed);
  return {
    name: fileNameWithoutExtension(fileName), definition,
    workspace: definitionWorkspace(definition),
    environments: { default: Object.fromEntries(referencedVariables(definition).map(variable => [variable, ''])) },
  };
}

export function downloadPipeline(pipeline: SavedPipeline): string {
  const envelope = {
    format: 'pipeline-builder/pipeline', formatVersion: 1, exportedAt: new Date().toISOString(),
    pipeline: pipeline.document,
  };
  const safeName = pipeline.document.name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').trim() || 'pipeline';
  const fileName = `${safeName}.json`;
  const url = URL.createObjectURL(new Blob([JSON.stringify(envelope, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
  return fileName;
}
