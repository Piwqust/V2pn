// Two collections:
// - docs: the user documentation, written for the site (src/docs/*.md).
// - guide: the provider guide, whose one source is docs/partners/README.ru.md and README.md in the repository. The
//   site renders them as they are; edit the guide there.
import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';
import { z } from 'astro/zod';

const docs = defineCollection({
  loader: glob({ pattern: '*.md', base: './src/docs' }),
  schema: z.object({ title: z.string(), description: z.string(), order: z.number() }),
});

const guide = defineCollection({
  loader: glob({ pattern: 'README*.md', base: '../docs/partners', generateId: ({ entry }) => (entry === 'README.md' ? 'en' : 'ru') }),
});

export const collections = { docs, guide };
