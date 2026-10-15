# hookjump spec

A language-neutral description of hookjump's core, so it can be re-implemented
(for example in Kotlin on Android) without reading the Python code.

- `ALGORITHM.md`: the rules. Units, statuses, lyrics parsing, line comparison,
  seek detection and the favorite part so far; chorus detection and target
  selection are added as each is designed.
- `vectors/`: JSON test cases, input and expected output. The Python tests load
  every file; another implementation must pass the same files unchanged.

## Vector files

Each file is an object with `about` (what it tests), `cases` (a list) and,
where needed, `input_format`. Every case has a unique `name`, its input fields
and `expected`.

- Times are integer milliseconds: `62500`, never `62.5`.
- Status values are the lowercase strings listed in `ALGORITHM.md`, section 2.
- Expected values are worked out by hand from `ALGORITHM.md`, never copied from
  a program's output, so a vector can catch the program being wrong.
- All lyrics are made up; real lyrics are never committed.
