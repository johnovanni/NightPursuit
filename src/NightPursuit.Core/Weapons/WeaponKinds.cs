namespace NightPursuit.Core.Weapons;

/// <summary>The weapons that can be dropped behind the car (one is selected at a time).</summary>
public enum RearWeapon { Oil, Smoke, Caltrops }

/// <summary>Every weapon that can announce itself in a WeaponUsed event.</summary>
public enum WeaponKind { Oil, Smoke, Caltrops, Missile, Strike }

/// <summary>Something a supply crate can restock.</summary>
public enum SupplyItem { Oil, Smoke, Caltrops, Missile, Strike, Armor }
