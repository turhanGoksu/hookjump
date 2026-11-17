using System;

namespace Hookjump.Core;

/// <summary>Decide where a jump lands, and say why (spec/ALGORITHM.md, section 5).</summary>
public static class Target
{
    // Targets are measured a little late: the key is pressed after the part
    // starts, and a seek's arrival is read at the next report. Landing this much
    // earlier keeps the first word.
    public const long LeadMs = 1500;

    /// <summary>The jump for the playing track: the pin, else the learned favorite.</summary>
    public static JumpResult Choose(bool trackLoaded, Pin? pin, Favorite? favorite)
    {
        if (!trackLoaded) return new JumpResult(Status.PlayerUnavailable);
        if (pin is not null) return new JumpResult(Status.Pinned, pin.Typed ? pin.AtMs : Lead(pin.AtMs));
        if (favorite is not null) return new JumpResult(Status.Personal, Lead(favorite.TargetMs));
        return new JumpResult(Status.NoData);
    }

    private static long Lead(long targetMs) => Math.Max(0, targetMs - LeadMs);
}
