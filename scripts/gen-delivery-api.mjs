#!/usr/bin/env node
// Generates the API's typed Umbraco Delivery API models from the CMS's OpenAPI document, the source of truth for the
// content model the Umbraco adapter reads.
//
//   node scripts/gen-delivery-api.mjs                         # regenerate the C# models from the committed spec
//   node scripts/gen-delivery-api.mjs --from http://localhost:12276
//                                                             # first refresh the spec from a running CMS
//
// The CMS publishes the spec at /umbraco/openapi/delivery.json with per-content-type schemas
// (Umbraco:CMS:DeliveryApi:OpenApi:GenerateContentTypeSchemas in src/Cms/appsettings.json). Schemas map to C# like so:
// - objects become sealed records; *PropertiesModel records keep properties the spec does not know yet in Additional;
// - a oneOf with a discriminator becomes an abstract record holding the members all variants share, one sealed
//   record per variant, and an Unknown* variant for discriminator values the spec does not know yet (a content type
//   added in the CMS before the spec is refreshed), deserialized by DiscriminatedUnionConverter;
// - enums are plain strings, so a new value never fails deserialization.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const specPath = join(root, 'src/Api/Content/Umbraco/delivery-api.json');
const outPath = join(root, 'src/Api/Content/Umbraco/DeliveryApiModels.g.cs');

const fromIndex = process.argv.indexOf('--from');
if (fromIndex > 0) {
  const url = new URL('/umbraco/openapi/delivery.json', process.argv[fromIndex + 1]);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`GET ${url}: ${response.status}`);
  writeFileSync(specPath, JSON.stringify(await response.json(), null, 2) + '\n');
  console.log(`Updated ${specPath} from ${url}.`);
}

const spec = JSON.parse(readFileSync(specPath, 'utf8'));
const schemas = spec.components.schemas;
const skip = new Set(['ProblemDetails']);

// "IApiContentModel" would read as an interface in C#.
const typeName = (name) => name.replace(/IApi/g, 'Api');
const pascal = (name) => name[0].toUpperCase() + name.slice(1);
const refName = (ref) => ref.split('/').pop();
const isEnum = (name) => Array.isArray(schemas[name]?.enum);
const isUnion = (schema) => Array.isArray(schema.oneOf) && schema.discriminator;

/** All properties of a schema including those composed in through allOf (later ones win). */
function flatten(schema) {
  const props = new Map();
  for (const part of schema.allOf ?? []) {
    for (const [key, value] of flatten(schemas[refName(part.$ref)])) props.set(key, value);
  }
  const required = new Set(schema.required ?? []);
  for (const [key, value] of Object.entries(schema.properties ?? {})) {
    props.set(key, { schema: value, required: required.has(key) || props.get(key)?.required === true });
  }
  return props;
}

/** C# type of a property schema, and whether it may be null. */
function csType(schema) {
  if (schema.$ref) {
    const name = refName(schema.$ref);
    return { type: isEnum(name) ? 'string' : typeName(name), nullable: true };
  }
  if (schema.oneOf) {
    const [single, ...rest] = schema.oneOf.filter((s) => s.type !== 'null');
    if (single && rest.length === 0) return { ...csType(single), nullable: true };
    return { type: 'JsonElement', nullable: true };
  }
  const types = [schema.type].flat().filter(Boolean);
  const nullable = types.includes('null');
  const kind = types.find((t) => t !== 'null');
  switch (kind) {
    case 'string':
      if (schema.format === 'uuid') return { type: 'Guid', nullable, value: true };
      if (schema.format === 'date-time') return { type: 'DateTimeOffset', nullable, value: true };
      return { type: 'string', nullable };
    case 'integer':
      return { type: schema.format === 'int64' ? 'long' : 'int', nullable, value: true };
    case 'number':
      return { type: 'double', nullable, value: true };
    case 'boolean':
      return { type: 'bool', nullable, value: true };
    case 'array':
      return { type: `${csType(schema.items).type}[]`, nullable };
    case 'object':
      return schema.additionalProperties?.$ref
        ? { type: `Dictionary<string, ${csType(schema.additionalProperties).type}>`, nullable }
        : { type: schema.additionalProperties ? 'Dictionary<string, JsonElement>' : 'JsonElement', nullable, value: !schema.additionalProperties };
    default:
      return { type: 'JsonElement', nullable: true, value: true };
  }
}

function property(key, { schema, required }) {
  const { type, nullable, value } = csType(schema);
  // References and value types missing from "required" are optional; Umbraco also returns null for some required
  // references (a block's settings), so those are nullable too.
  const optional = nullable || !required || schema.$ref || schema.oneOf;
  const declared = optional ? `${type}?` : type;
  const init = optional || value ? '' : type.endsWith('[]') ? ' = [];' : type === 'string' ? ' = string.Empty;' : ' = default!;';
  return [`    [JsonPropertyName("${key}")]`, `    public ${declared} ${pascal(key)} { get; init; }${init}`];
}

const sameSchema = (a, b) => JSON.stringify(a.schema) === JSON.stringify(b.schema);

const lines = [
  '// <auto-generated>',
  '// Generated by scripts/gen-delivery-api.mjs from delivery-api.json (the Umbraco Delivery API OpenAPI document).',
  '// Do not edit by hand: change the content model in the CMS, then regenerate.',
  '// </auto-generated>',
  '#nullable enable',
  'using System.Text.Json;',
  'using System.Text.Json.Serialization;',
  '',
  'namespace Api.Content.Umbraco.Delivery;',
];

// A blank line between members: before each attribute or doc comment that does not follow a doc comment.
const startsMember = (line) => line.startsWith('    [') || line.startsWith('    ///');
const record = (header, body) =>
  lines.push('', header, '{', ...body.flatMap((l, i) => (i && startsMember(l) && !body[i - 1].startsWith('    ///') ? ['', l] : [l])), '}');

const variantOf = new Map(); // variant schema name -> union name
for (const [name, schema] of Object.entries(schemas)) {
  if (isUnion(schema)) for (const v of schema.oneOf) variantOf.set(refName(v.$ref), name);
}

for (const [name, schema] of Object.entries(schemas)) {
  if (skip.has(name) || isEnum(name) || variantOf.has(name) || name.endsWith('BaseModel')) continue;

  if (isUnion(schema)) {
    const discriminator = schema.discriminator.propertyName;
    const variants = Object.entries(schema.discriminator.mapping).map(([value, ref]) => ({
      value,
      name: refName(ref),
      props: flatten(schemas[refName(ref)]),
    }));
    // Members every variant shares with the same schema; the discriminator itself is a plain string.
    const [first] = variants;
    const shared = [...first.props].filter(
      ([key, p]) => key !== discriminator && variants.every((v) => v.props.has(key) && sameSchema(v.props.get(key), p)),
    );
    const sharedKeys = new Set([discriminator, ...shared.map(([key]) => key)]);
    const base = typeName(name);
    const unknown = `Unknown${base}`;

    record(
      `[JsonConverter(typeof(DiscriminatedUnionConverter<${base}>))]\npublic abstract record ${base} : IDiscriminatedUnion`,
      [
        `    public static string Discriminator => "${discriminator}";`,
        '',
        '    public static Type Resolve(string? value) => value switch',
        '    {',
        ...variants.map((v) => `        "${v.value}" => typeof(${typeName(v.name)}),`),
        `        _ => typeof(${unknown}),`,
        '    };',
        ...property(discriminator, { schema: { type: 'string' }, required: true }),
        ...shared.flatMap(([key, p]) => property(key, p)),
      ],
    );
    for (const v of variants) {
      record(
        `public sealed record ${typeName(v.name)} : ${base}`,
        [...v.props].filter(([key]) => !sharedKeys.has(key)).flatMap(([key, p]) => property(key, p)),
      );
    }
    // Variant-specific members (the typed "properties") arrive untyped for a type the spec does not know.
    const rest = [...new Set(variants.flatMap((v) => [...v.props.keys()]))].filter((key) => !sharedKeys.has(key));
    record(
      `/// <summary>A ${discriminator} the spec does not know yet.</summary>\npublic sealed record ${unknown} : ${base}`,
      rest.flatMap((key) => property(key, { schema: { type: ['null', 'object'] }, required: false })),
    );
    continue;
  }

  const body = [...flatten(schema)].flatMap(([key, p]) => property(key, p));
  if (name.endsWith('PropertiesModel')) {
    body.push('    /// <summary>Properties the spec does not know yet.</summary>', '    [JsonExtensionData]',
      '    public Dictionary<string, JsonElement>? Additional { get; init; }');
  }
  record(`public sealed record ${typeName(name)}`, body);
}

writeFileSync(outPath, lines.join('\n') + '\n');
console.log(`Generated ${outPath}.`);
