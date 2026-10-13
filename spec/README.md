# hookjump spec

A language-neutral description of hookjump's core, so it can be re-implemented
(for example in Kotlin on Android) without reading the Python code.

- `ALGORITHM.md`: the rules. Units and statuses first; lyrics parsing, chorus
  detection, seek detection and heatmap scoring are added as each is designed.
- `vectors/` (added with the first algorithm): JSON test cases, input and
  expected output. The Python tests load every file; another implementation
  must pass the same files unchanged.

Conventions for every vector:

- Times are integer milliseconds: `62500`, never `62.5`.
- Status values are the lowercase strings listed in `ALGORITHM.md`, section 2.
