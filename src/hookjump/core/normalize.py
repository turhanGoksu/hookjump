"""Comparison keys for lyric lines (spec/ALGORITHM.md, section 4).

Two lines are "the same line" when their keys are equal. A key is only for
comparing: it is never shown and never stored.
"""

import re
import unicodedata

# An innermost bracket group: (...) or [...] with no bracket of its kind inside.
_GROUP = re.compile(r"\([^()]*\)|\[[^\[\]]*\]")
_BRACKETS = re.compile(r"[()\[\]]")
# Removed, not turned into spaces: "don't" -> "dont", "Ali'nin" -> "alinin".
_APOSTROPHES = re.compile("['‘’`´ʼ]")


def line_key(text: str) -> str:
    """Lowercase ASCII-folded words of a lyric line, for comparing lines.

    Ad-libs in brackets are dropped ("love (oh-i-oh)" equals "love"), unless
    the whole line is bracketed: a backing-vocal line keeps its words.
    """
    outside = text
    while (dropped := _GROUP.sub(" ", outside)) != outside:
        outside = dropped
    if _has_word_char(outside):
        text = outside
    else:
        text = _BRACKETS.sub(" ", text)

    # Apostrophes go first: NFKD turns "´" into a space and a combining mark.
    text = _APOSTROPHES.sub("", text)
    # NFKD splits "ş" into "s" and a combining cedilla, and "İ" into "I" and a
    # combining dot; dropping the marks leaves plain letters. Only the dotless
    # "ı" has no decomposition, so it is mapped by hand.
    text = unicodedata.normalize("NFKD", text.replace("ı", "i"))
    text = "".join(c for c in text if unicodedata.category(c) != "Mn").lower()
    return " ".join("".join(c if _is_word_char(c) else " " for c in text).split())


def _is_word_char(c: str) -> bool:
    # Letters and decimal digits, by Unicode category: other languages agree on
    # these, while Python's isalnum() also accepts numerals such as "〇".
    category = unicodedata.category(c)
    return category[0] == "L" or category == "Nd"


def _has_word_char(text: str) -> bool:
    return any(_is_word_char(c) for c in text)
