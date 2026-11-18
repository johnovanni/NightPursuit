using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Hazards;

/// <summary>A crate lying in a lane. Only the player picks it up; it restocks one weapon or repairs armor.</summary>
public sealed class SupplyCrate(SupplyItem item, int amount) : Hazard
{
    public const double Size = 1.6;
    public const double ClearBehind = 600;
    public const int ArmorPerAmount = 25;

    public SupplyItem Item { get; } = item;
    public int Amount { get; } = amount;

    /// <summary>Crates never move; kept so every road object can be drawn the same way (it equals <see cref="Hazard.Distance"/>).</summary>
    public double PrevDistance { get; set; }

    /// <summary>"OIL +2", "ARMOR +25": the item and how much it gives, shown on the crate.</summary>
    public string Label => $"{Item.ToString().ToUpperInvariant()} +{(Item == SupplyItem.Armor ? ArmorPerAmount * Amount : Amount)}";

    public override void Update(double dt, double playerDistance)
    {
        if (Distance < playerDistance - ClearBehind) Expired = true;
    }
}
