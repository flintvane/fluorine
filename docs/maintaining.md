<p align="center"><img src="logo.png" alt="Fluorine" width="160"></p>

# Maintaining these docs

User documentation for the C# implementation of Fluorine (.NET Framework 4.8).

## Structure

- `docs/index.md`: the master list. An ordered list of topics, each with a short description and a link to its page. This is the file to edit.
- `topics/<slug>.md`: one page per topic, written out in detail. Each page starts with a navigation line (previous, list, next) and ends with references to the spec.
- `logo.png`: copy of `repo/branding/fluorine-logo.png`.

## Sources

The pages follow, in this order of authority:

1. `spec/semantics-cs.md` (C# realisation, draft 0.6): the API as users see it.
2. `spec/semantics.md` (platform-neutral semantics, draft 0.10).

References such as *(cs 2.7)* point to `semantics-cs.md`; *(§4.1)* points to `semantics.md`. Points the spec leaves open are marked **TODO** with a spec reference.

## Regeneration rule

When `docs/index.md` changes:

1. A new line: create `topics/<slug>.md` for its link, written in the style of the other pages.
2. A removed line: delete its page, and remove links to it from other pages.
3. A reordered list: update the previous/next navigation line on the affected pages.
4. A changed description: check that the page still covers it.
5. A spec change: update the affected pages and their references.

Style: state what is, positively and plainly. Code uses only API named in `semantics-cs.md`.
