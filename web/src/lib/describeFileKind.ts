import { ItemKind } from "../api/channeled";

export type FileKindCategory =
  | "image"
  | "video"
  | "audio"
  | "document"
  | "spreadsheet"
  | "presentation"
  | "archive"
  | "code"
  | "text";

/** Extension (lowercase, no dot) → category. Prefer longer compound exts via longest-match lookup. */
const EXT_CATEGORY: Record<string, FileKindCategory> = {
  // image
  png: "image",
  jpg: "image",
  jpeg: "image",
  jpe: "image",
  jfif: "image",
  webp: "image",
  gif: "image",
  svg: "image",
  svgz: "image",
  tif: "image",
  tiff: "image",
  bmp: "image",
  dib: "image",
  heic: "image",
  heif: "image",
  avif: "image",
  ico: "image",
  icns: "image",
  raw: "image",
  cr2: "image",
  nef: "image",
  arw: "image",
  dng: "image",
  psd: "image",
  ai: "image",
  eps: "image",
  // video
  mp4: "video",
  m4v: "video",
  webm: "video",
  mov: "video",
  qt: "video",
  mkv: "video",
  avi: "video",
  m2ts: "video",
  mts: "video",
  // .ts → code by default (TypeScript); MPEG-TS via video/* MIME
  mpeg: "video",
  mpg: "video",
  mpe: "video",
  mp2: "video",
  wmv: "video",
  flv: "video",
  f4v: "video",
  "3gp": "video",
  "3g2": "video",
  ogv: "video",
  vob: "video",
  // audio
  mp3: "audio",
  wav: "audio",
  wave: "audio",
  flac: "audio",
  aac: "audio",
  ogg: "audio",
  oga: "audio",
  opus: "audio",
  m4a: "audio",
  wma: "audio",
  aiff: "audio",
  aif: "audio",
  aifc: "audio",
  mid: "audio",
  midi: "audio",
  amr: "audio",
  ape: "audio",
  // document
  pdf: "document",
  docx: "document",
  doc: "document",
  docm: "document",
  dot: "document",
  dotx: "document",
  odt: "document",
  ott: "document",
  rtf: "document",
  pages: "document",
  epub: "document",
  mobi: "document",
  djvu: "document",
  // spreadsheet
  xlsx: "spreadsheet",
  xls: "spreadsheet",
  xlsm: "spreadsheet",
  xlsb: "spreadsheet",
  ods: "spreadsheet",
  ots: "spreadsheet",
  csv: "spreadsheet",
  tsv: "spreadsheet",
  numbers: "spreadsheet",
  // presentation
  pptx: "presentation",
  ppt: "presentation",
  pptm: "presentation",
  pps: "presentation",
  ppsx: "presentation",
  odp: "presentation",
  otp: "presentation",
  key: "presentation",
  // archive
  zip: "archive",
  "7z": "archive",
  rar: "archive",
  tar: "archive",
  gz: "archive",
  tgz: "archive",
  bz2: "archive",
  tbz2: "archive",
  xz: "archive",
  txz: "archive",
  lz: "archive",
  lzma: "archive",
  zst: "archive",
  cab: "archive",
  iso: "archive",
  dmg: "archive",
  jar: "archive",
  war: "archive",
  // code
  php: "code",
  phtml: "code",
  c: "code",
  cc: "code",
  cpp: "code",
  cxx: "code",
  h: "code",
  hh: "code",
  hpp: "code",
  hxx: "code",
  cs: "code",
  java: "code",
  kt: "code",
  kts: "code",
  scala: "code",
  groovy: "code",
  js: "code",
  mjs: "code",
  cjs: "code",
  ts: "code", // TypeScript; also video MPEG-TS — resolved by MIME when present
  jsx: "code",
  tsx: "code",
  html: "code",
  htm: "code",
  xhtml: "code",
  css: "code",
  scss: "code",
  sass: "code",
  less: "code",
  sql: "code",
  json: "code",
  jsonc: "code",
  json5: "code",
  xml: "code",
  xsd: "code",
  xsl: "code",
  xslt: "code",
  yaml: "code",
  yml: "code",
  toml: "code",
  ini: "code",
  conf: "code",
  cfg: "code",
  env: "code",
  py: "code",
  pyw: "code",
  pyi: "code",
  rb: "code",
  go: "code",
  rs: "code",
  swift: "code",
  m: "code",
  mm: "code",
  sh: "code",
  bash: "code",
  zsh: "code",
  fish: "code",
  ps1: "code",
  psm1: "code",
  bat: "code",
  cmd: "code",
  vue: "code",
  svelte: "code",
  dart: "code",
  lua: "code",
  r: "code",
  pl: "code",
  pm: "code",
  ex: "code",
  exs: "code",
  erl: "code",
  hs: "code",
  elm: "code",
  clj: "code",
  cljs: "code",
  fs: "code",
  fsx: "code",
  vb: "code",
  proto: "code",
  graphql: "code",
  gql: "code",
  tf: "code",
  dockerfile: "code",
  makefile: "code",
  cmake: "code",
  gradle: "code",
  // text
  txt: "text",
  text: "text",
  md: "text",
  markdown: "text",
  mdown: "text",
  log: "text",
  rst: "text",
  adoc: "text",
  asciidoc: "text",
  org: "text",
  tex: "text",
  ltx: "text",
  csvg: "text",
};

function extensionFromName(name: string): string | null {
  const base = name.trim().split(/[/\\]/).pop() ?? name.trim();
  if (!base) return null;

  const lower = base.toLowerCase();
  if (lower.endsWith(".tar.gz") || lower.endsWith(".tgz")) return "tgz";
  if (lower.endsWith(".tar.bz2") || lower.endsWith(".tbz2")) return "tbz2";
  if (lower.endsWith(".tar.xz") || lower.endsWith(".txz")) return "txz";
  if (lower.endsWith(".tar.zst")) return "zst";

  const dot = lower.lastIndexOf(".");
  if (dot <= 0 || dot === lower.length - 1) {
    if (lower === "dockerfile" || lower === "makefile") return lower;
    return null;
  }
  return lower.slice(dot + 1);
}

function normalizeMime(value: string): string | null {
  const mime = value.toLowerCase().split(";")[0]?.trim() ?? "";
  if (!mime.includes("/")) return null;
  return mime;
}

function categoryFromMime(mime: string): FileKindCategory | null {
  const [type, subtypeRaw = ""] = mime.split("/");
  const subtype = subtypeRaw.replace(/^x-/, "");

  if (type === "image") return "image";
  if (type === "video") return "video"; // includes video/mp2t (MPEG-TS)
  if (type === "audio") return "audio";

  if (type === "text") {
    if (subtype === "csv" || subtype === "tab-separated-values") return "spreadsheet";
    if (
      subtype === "html" ||
      subtype === "css" ||
      subtype === "javascript" ||
      subtype === "ecmascript" ||
      subtype === "xml" ||
      subtype === "x-python" ||
      subtype === "x-sh" ||
      subtype === "x-script.python" ||
      subtype === "jsx" ||
      subtype === "tsx"
    ) {
      return "code";
    }
    if (
      subtype === "plain" ||
      subtype === "markdown" ||
      subtype === "x-markdown" ||
      subtype === "x-log" ||
      subtype === "richtext"
    ) {
      return "text";
    }
    // generic text/* → text
    return "text";
  }

  if (type === "application") {
    if (subtype === "pdf") return "document";
    if (
      subtype === "msword" ||
      subtype.includes("wordprocessingml") ||
      subtype === "rtf" ||
      subtype === "vnd.oasis.opendocument.text" ||
      subtype === "epub+zip"
    ) {
      return "document";
    }
    if (
      subtype === "vnd.ms-excel" ||
      subtype.includes("spreadsheetml") ||
      subtype === "vnd.oasis.opendocument.spreadsheet" ||
      subtype === "csv"
    ) {
      return "spreadsheet";
    }
    if (
      subtype === "vnd.ms-powerpoint" ||
      subtype.includes("presentationml") ||
      subtype === "vnd.oasis.opendocument.presentation"
    ) {
      return "presentation";
    }
    if (
      subtype === "zip" ||
      subtype === "x-zip-compressed" ||
      subtype === "x-7z-compressed" ||
      subtype === "vnd.rar" ||
      subtype === "x-rar-compressed" ||
      subtype === "x-tar" ||
      subtype === "gzip" ||
      subtype === "x-gzip" ||
      subtype === "x-bzip2" ||
      subtype === "x-xz" ||
      subtype === "zstd" ||
      subtype === "x-iso9660-image" ||
      subtype === "x-apple-diskimage" ||
      subtype === "java-archive"
    ) {
      return "archive";
    }
    if (
      subtype === "json" ||
      subtype === "ld+json" ||
      subtype === "xml" ||
      subtype === "javascript" ||
      subtype === "ecmascript" ||
      subtype === "typescript" ||
      subtype === "sql" ||
      subtype === "x-httpd-php" ||
      subtype === "x-sh" ||
      subtype === "x-bash" ||
      subtype === "x-powershell" ||
      subtype === "yaml" ||
      subtype === "x-yaml" ||
      subtype === "toml" ||
      subtype === "graphql" ||
      subtype === "wasm"
    ) {
      return "code";
    }
  }

  if (type === "model") return "image"; // glTF etc. often treated as assets; leave uncategorized? skip
  return null;
}

/**
 * Kind for the details panel: Folder, a category (image/video/…), or the file extension.
 * Uses MIME when present, then extension.
 */
export function describeFileKind(item: {
  kind: number;
  name: string;
  contentType?: string | null;
}): string {
  if (item.kind === ItemKind.Folder) {
    return "Folder";
  }

  const mime = item.contentType ? normalizeMime(item.contentType) : null;
  if (mime) {
    const fromMime = categoryFromMime(mime);
    if (fromMime) return fromMime;
  }

  const ext = extensionFromName(item.name);
  if (ext) {
    // Prefer MIME for ambiguous .ts (TypeScript vs MPEG-TS)
    if (ext === "ts" && mime?.startsWith("video/")) return "video";
    if (ext === "ts" && mime?.includes("typescript")) return "code";
    if (ext === "ts" && !mime) return "code";

    const fromExt = EXT_CATEGORY[ext];
    if (fromExt) return fromExt;
    return ext.toUpperCase();
  }

  if (mime && mime !== "application/octet-stream") {
    return mime;
  }

  return "file";
}
