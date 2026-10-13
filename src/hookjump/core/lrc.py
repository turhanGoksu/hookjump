"""Parse LRC time-synced lyrics into timed lines (spec/ALGORITHM.md, section 3)."""

import re

from hookjump.core.models import LyricLine, ParsedLyrics

# [0-9], not \d: Python's \d also matches non-ASCII digits, other languages' not.
_LINE_BREAK = re.compile(r"\r\n|\r|\n")
_STAMP = re.compile(r"\[([0-9]+):([0-9]{2})(?:[.:]([0-9]{1,3}))?\]")
_WORD_STAMP = re.compile(r"<[0-9]+:[0-9]{2}(?:[.:][0-9]{1,3})?>")
_ID_TAG = re.compile(r"\[([A-Za-z#]+):(.*)\]")
_OFFSET_VALUE = re.compile(r"[+-]?[0-9]+")


def parse_lrc(text: str) -> ParsedLyrics:
    """Read LRC text: timed lines sorted by start time, and a skipped count.

    Blank lines and ID tags such as ``[ar:...]`` are ignored; any other line
    that is not timed counts as skipped.
    """
    timed: list[LyricLine] = []
    skipped = 0
    offset_ms = 0
    for raw in _LINE_BREAK.split(text.removeprefix("﻿")):
        line = raw.strip()
        if not line:
            continue

        stamps = []
        end = 0
        while match := _STAMP.match(line, end):
            stamps.append(match)
            end = match.end()
        if stamps:
            if any(int(m[2]) >= 60 for m in stamps):
                skipped += 1
                continue
            words = _WORD_STAMP.sub("", line[end:]).strip()
            timed.extend(LyricLine(_stamp_ms(m), words) for m in stamps)
            continue

        tag = _ID_TAG.fullmatch(line)
        if tag is None:
            skipped += 1
        elif tag[1].lower() == "offset":
            value = tag[2].strip()
            if _OFFSET_VALUE.fullmatch(value):
                offset_ms = int(value)  # the last offset tag wins
            else:
                skipped += 1

    # A positive offset shows lyrics earlier. sorted() is stable, so lines with
    # the same start keep their file order.
    shifted = (LyricLine(max(0, ln.start_ms - offset_ms), ln.text) for ln in timed)
    return ParsedLyrics(tuple(sorted(shifted, key=lambda ln: ln.start_ms)), skipped)


def _stamp_ms(match: re.Match[str]) -> int:
    minutes, seconds, fraction = match[1], match[2], match[3] or ""
    # The fraction is a decimal part of a second: ".5" and ".500" are 500 ms.
    return int(minutes) * 60_000 + int(seconds) * 1000 + int(fraction.ljust(3, "0"))
