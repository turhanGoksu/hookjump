using System;
using System.Collections.Generic;
using System.Linq;

namespace Hookjump.Core;

/// <summary>
/// Find the listener's favorite part of a track from their seeks (spec/ALGORITHM.md, section 4).
/// </summary>
/// <remarks>
/// Untouched playback says nothing about taste, so only the listener's own
/// moves count: where they rewound to, and where they skipped forward to.
/// </remarks>
public static class Heatmap
{
    public const int RewindPoints = 3; // "I want to hear this again": the strongest signal
    public const int ForwardPoints = 2; // "I want to get here", but also just looking around
    public const long RegionGapMs = 5000; // arrivals closer than this are the same place
    public const int MinPoints = 5;
    public const int MinListens = 2; // one stray seek, or many in one sitting, is not a habit

    // Only the listens in which the listener seeked count, the most recent ones
    // first. Listens without seeks (untouched, or jumped by hookjump) never push old
    // seeks out: otherwise a learned favorite would fade exactly because the key
    // now does the seeking. New seeks do, so a change of taste is followed.
    public const int RecentListens = 10;

    /// <summary>The favorite part from all seeks of one track, or null if unsure yet.</summary>
    /// <remarks>Listen numbers must grow with time: a later listen has a higher number.</remarks>
    public static Favorite? Favorite(IReadOnlyCollection<SeekEvent> seeks)
    {
        if (seeks.Select(s => s.TrackId).Distinct().Count() > 1)
            throw new ArgumentException("Favorite() takes the seeks of one track", nameof(seeks));
        var recent = seeks.Select(s => s.Listen).Distinct().Order().TakeLast(RecentListens).ToHashSet();

        // Group arrival points into regions: a gap of more than RegionGapMs between
        // neighbours starts a new region. OrderBy is stable, as the spec needs.
        var regions = new List<List<SeekEvent>>();
        foreach (var seek in seeks.Where(s => recent.Contains(s.Listen)).OrderBy(s => s.ToMs))
        {
            if (regions.Count > 0 && seek.ToMs - regions[^1][^1].ToMs <= RegionGapMs)
                regions[^1].Add(seek);
            else
                regions.Add(new List<SeekEvent> { seek }); // not [seek]: that brings in InteropServices
        }

        Favorite? best = null;
        foreach (var region in regions) // in time order: on a full tie the earlier one stays
        {
            var points = region.Sum(s => s.ToMs < s.FromMs ? RewindPoints : ForwardPoints);
            var listens = region.Select(s => s.Listen).Distinct().Count();
            if (points < MinPoints || listens < MinListens) continue;
            if (best is null || points > best.Points || (points == best.Points && listens > best.Listens))
            {
                // The lower median: always a point the listener actually chose.
                best = new Favorite(region[(region.Count - 1) / 2].ToMs, points, listens);
            }
        }
        return best;
    }
}
