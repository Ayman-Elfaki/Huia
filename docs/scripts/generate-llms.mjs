import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, '..');
const repoRootDir = path.resolve(docsDir, '..');
const publicDir = path.resolve(docsDir, 'public');

if (!fs.existsSync(publicDir)) {
  fs.mkdirSync(publicDir, { recursive: true });
}

// Sidebar hierarchy matching docs/.vitepress/config.ts
const structure = [
  {
    section: 'Overview',
    items: [
      { title: 'Huia Overview & Features', link: '/index', file: 'index.md' },
    ],
  },
  {
    section: 'Guide',
    items: [
      { title: 'Getting started (OpenID)', link: '/guide/getting-started', file: 'guide/getting-started.md' },
      { title: 'Getting started (Headless)', link: '/guide/getting-started-headless', file: 'guide/getting-started-headless.md' },
      { title: 'Configuration', link: '/guide/configuration', file: 'guide/configuration.md' },
      { title: 'Multi-tenancy', link: '/guide/multi-tenancy', file: 'guide/multi-tenancy.md' },
      { title: 'Pushed authorization (PAR)', link: '/guide/pushed-authorization', file: 'guide/pushed-authorization.md' },
      { title: 'Admin console', link: '/guide/admin-ui', file: 'guide/admin-ui.md' },
      { title: 'Email testing', link: '/guide/email-testing', file: 'guide/email-testing.md' },
    ],
  },
  {
    section: '.NET reference — Huia.OpenId',
    items: [
      { title: 'Options reference', link: '/dotnet/options', file: 'dotnet/options.md' },
      { title: 'HuiaUserManager & user types', link: '/dotnet/user-manager', file: 'dotnet/user-manager.md' },
      { title: 'Endpoints', link: '/dotnet/endpoints', file: 'dotnet/endpoints.md' },
      { title: 'Passwordless SMS', link: '/dotnet/passwordless-sms', file: 'dotnet/passwordless-sms.md' },
      { title: 'Passkeys', link: '/dotnet/passkeys', file: 'dotnet/passkeys.md' },
      { title: 'External login', link: '/dotnet/external-login', file: 'dotnet/external-login.md' },
      { title: 'Events', link: '/dotnet/events', file: 'dotnet/events.md' },
    ],
  },
  {
    section: '.NET reference — Huia.Headless',
    items: [
      { title: 'Huia.Headless', link: '/dotnet/headless', file: 'dotnet/headless.md' },
    ],
  },
  {
    section: 'Shared Core (huia-auth-core)',
    items: [
      { title: 'Overview', link: '/core/overview', file: 'core/overview.md' },
    ],
  },
  {
    section: 'Next.js library — OIDC (next-huia-oidc)',
    items: [
      { title: 'Overview', link: '/next/overview', file: 'next/overview.md' },
      { title: 'Configuration', link: '/next/configuration', file: 'next/configuration.md' },
      { title: 'Session model & security', link: '/next/session-model', file: 'next/session-model.md' },
      { title: 'Route protection', link: '/next/route-protection', file: 'next/route-protection.md' },
      { title: 'Migrating from NextAuth', link: '/next/migrating', file: 'next/migrating.md' },
    ],
  },
  {
    section: 'Next.js library — Headless (next-huia-headless)',
    items: [
      { title: 'Overview', link: '/next-headless/overview', file: 'next-headless/overview.md' },
      { title: 'Configuration', link: '/next-headless/configuration', file: 'next-headless/configuration.md' },
    ],
  },
  {
    section: 'Nuxt module — OIDC (nuxt-huia-oidc)',
    items: [
      { title: 'Overview', link: '/nuxt/overview', file: 'nuxt/overview.md' },
      { title: 'Configuration', link: '/nuxt/configuration', file: 'nuxt/configuration.md' },
      { title: 'Session model & security', link: '/nuxt/session-model', file: 'nuxt/session-model.md' },
      { title: 'Route protection', link: '/nuxt/route-protection', file: 'nuxt/route-protection.md' },
      { title: 'Migrating from nuxt-oidc-auth', link: '/nuxt/migrating', file: 'nuxt/migrating.md' },
    ],
  },
  {
    section: 'Nuxt module — Headless (nuxt-huia-headless)',
    items: [
      { title: 'Overview', link: '/nuxt-headless/overview', file: 'nuxt-headless/overview.md' },
      { title: 'Configuration', link: '/nuxt-headless/configuration', file: 'nuxt-headless/configuration.md' },
    ],
  },
  {
    section: 'Architecture',
    items: [
      { title: 'Overview', link: '/architecture/overview', file: 'architecture/overview.md' },
      { title: 'Request & token flow', link: '/architecture/request-flow', file: 'architecture/request-flow.md' },
      { title: 'Key management', link: '/architecture/key-management', file: 'architecture/key-management.md' },
    ],
  },
  {
    section: 'Security',
    items: [
      { title: 'Overview', link: '/security/index', file: 'security/index.md' },
      { title: 'Security headers', link: '/security/security-headers', file: 'security/security-headers.md' },
    ],
  },
];

function cleanMarkdown(rawContent, filePath) {
  let content = rawContent;

  // Handle index.md frontmatter specifically
  if (filePath === 'index.md') {
    // Extract tagline and features if available
    const taglineMatch = content.match(/tagline:\s*([^\n\r]+)/);
    const tagline = taglineMatch ? taglineMatch[1].trim() : '';

    const featuresBlockMatch = content.match(/features:\s*\n([\s\S]*?)---/);
    let featuresText = '';
    if (featuresBlockMatch) {
      const rawFeatures = featuresBlockMatch[1];
      const featureMatches = [...rawFeatures.matchAll(/-\s*title:\s*([^\n\r]+)\s*\n\s*details:\s*([^\n\r]+)/g)];
      if (featureMatches.length > 0) {
        featuresText = '### Key Features\n\n' + featureMatches
          .map(m => `- **${m[1].trim()}**: ${m[2].trim()}`)
          .join('\n');
      }
    }

    // Strip YAML frontmatter
    content = content.replace(/^---[\s\S]*?---\s*/, '');

    const prefix = [
      '# Huia',
      '',
      tagline ? `> ${tagline}\n` : '',
      featuresText ? `${featuresText}\n` : '',
    ].filter(Boolean).join('\n');

    content = prefix + (content ? '\n\n' + content : '');
  } else {
    // General YAML frontmatter stripping
    content = content.replace(/^---[\s\S]*?---\s*/, '');
  }

  // Convert VitePress custom containers: ::: tip / ::: warning to blockquotes
  content = content.replace(/:::\s*tip(?:\s+([^\n\r]*))?\n([\s\S]*?):::/g, (_, title, body) => {
    const heading = title ? `> **Tip: ${title.trim()}**\n>\n` : `> **Tip**\n>\n`;
    const formattedBody = body.trim().split('\n').map(l => `> ${l}`).join('\n');
    return `${heading}${formattedBody}\n`;
  });

  content = content.replace(/:::\s*warning(?:\s+([^\n\r]*))?\n([\s\S]*?):::/g, (_, title, body) => {
    const heading = title ? `> **Warning: ${title.trim()}**\n>\n` : `> **Warning**\n>\n`;
    const formattedBody = body.trim().split('\n').map(l => `> ${l}`).join('\n');
    return `${heading}${formattedBody}\n`;
  });

  // Clean trailing spaces and excessive blank lines
  content = content.replace(/\r\n/g, '\n');
  content = content.replace(/\n{3,}/g, '\n\n');

  return content.trim();
}

function generateLlmsFull() {
  const parts = [];

  parts.push('# Huia — Complete Documentation');
  parts.push('');
  parts.push('> Multi-tenant OpenID Connect / OAuth 2.0 (`Huia.OpenId`) and single-tenant bearer-token (`Huia.Headless`) identity for ASP.NET Core, with first-party Next.js (`next-huia-*`) and Nuxt 4 (`nuxt-huia-*`) client libraries.');
  parts.push('');
  parts.push('This document contains the complete official documentation for the Huia identity framework, formatted for consumption by Large Language Models (LLMs).');
  parts.push('');
  parts.push('---');
  parts.push('');
  parts.push('## Table of Contents');
  parts.push('');

  for (const group of structure) {
    parts.push(`### ${group.section}`);
    for (const item of group.items) {
      parts.push(`- [${item.title}](#${slugify(`${group.section} - ${item.title}`)})`);
    }
    parts.push('');
  }

  parts.push('---');
  parts.push('');

  for (const group of structure) {
    for (const item of group.items) {
      const fullPath = path.resolve(docsDir, item.file);
      if (!fs.existsSync(fullPath)) {
        console.warn(`File not found: ${fullPath}`);
        continue;
      }

      const raw = fs.readFileSync(fullPath, 'utf8');
      const cleaned = cleanMarkdown(raw, item.file);
      const sectionAnchor = slugify(`${group.section} - ${item.title}`);

      parts.push(`<!-- anchor: ${sectionAnchor} -->`);
      parts.push(`## [${group.section}] ${item.title}`);
      parts.push(`_Source: docs/${item.file}_`);
      parts.push('');
      parts.push(cleaned);
      parts.push('');
      parts.push('---');
      parts.push('');
    }
  }

  return parts.join('\n');
}

function generateLlmsTxt() {
  const parts = [];

  parts.push('# Huia');
  parts.push('');
  parts.push('> Multi-tenant OpenID Connect / OAuth 2.0 (Huia.OpenId) and single-tenant bearer-token (Huia.Headless) identity for ASP.NET Core, with first-party Next.js and Nuxt 4 client libraries.');
  parts.push('');
  parts.push('Huia provides end-to-end identity infrastructure spanning backend (.NET 10 / OpenIddict / EF Core) and frontend (Next.js 14/15 and Nuxt 4), with strict tenant isolation, automated key rotation, and secure encrypted session management.');
  parts.push('');
  parts.push('## Documentation');
  parts.push('');

  for (const group of structure) {
    parts.push(`### ${group.section}`);
    for (const item of group.items) {
      parts.push(`- [${item.title}](${item.link}): ${item.title} documentation`);
    }
    parts.push('');
  }

  parts.push('## Optional');
  parts.push('');
  parts.push('- [Full Documentation](/llms-full.txt): Complete concatenated documentation for LLM consumption in a single file');
  parts.push('');

  return parts.join('\n');
}

function slugify(text) {
  return text
    .toLowerCase()
    .replace(/[^\w\s-]/g, '')
    .trim()
    .replace(/[\s_]+/g, '-');
}

const llmsFullContent = generateLlmsFull();
const llmsContent = generateLlmsTxt();

// Write to docs/public/ for VitePress serving and distribution
const publicLlmsFull = path.join(publicDir, 'llms-full.txt');
const publicLlms = path.join(publicDir, 'llms.txt');
fs.writeFileSync(publicLlmsFull, llmsFullContent, 'utf8');
fs.writeFileSync(publicLlms, llmsContent, 'utf8');
console.log(`Generated: ${publicLlmsFull} (${llmsFullContent.length} bytes)`);
console.log(`Generated: ${publicLlms} (${llmsContent.length} bytes)`);

// Also mirror to repository root for direct access from repo root
const rootLlmsFull = path.join(repoRootDir, 'llms-full.txt');
const rootLlms = path.join(repoRootDir, 'llms.txt');
fs.writeFileSync(rootLlmsFull, llmsFullContent, 'utf8');
fs.writeFileSync(rootLlms, llmsContent, 'utf8');
console.log(`Generated: ${rootLlmsFull} (${llmsFullContent.length} bytes)`);
console.log(`Generated: ${rootLlms} (${llmsContent.length} bytes)`);
