"""The listener's favorite part, checked against spec/vectors/heatmap_favorite.json."""

from dataclasses import asdict

import pytest
from spec_vectors import cases

from hookjump.core.heatmap import favorite
from hookjump.core.models import SeekEvent


@pytest.mark.parametrize("case", cases("heatmap_favorite.json"))
def test_favorite(case: dict) -> None:
    seeks = [SeekEvent(track_id="a", **s) for s in case["seeks"]]
    found = favorite(seeks)
    assert (asdict(found) if found else None) == case["expected"]


def test_seeks_of_several_tracks_are_refused() -> None:
    seeks = [SeekEvent("a", 1, 9000, 1000), SeekEvent("b", 1, 9000, 1000)]
    with pytest.raises(ValueError, match="one track"):
        favorite(seeks)
