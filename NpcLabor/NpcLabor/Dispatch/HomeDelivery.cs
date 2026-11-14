using System;

namespace NpcLabor;

/// <summary>
/// Hands settled parcels to the player through vanilla's own home delivery.
///
/// Vanilla never walks up to a mailbox to hand over money: the tax bill and the
/// salary parcel both go out as <c>EClass.world.SendPackage(t)</c>
/// (<c>Faction.Economy -&gt; TryPayBill</c>, and the salary branch just above it),
/// which is literally <c>EClass.game.cards.listPackage.Add(t)</c>.
///
/// That list lives in the save and drains at 06:00 through
/// <c>Zone.OnSimulateHour -&gt; FactionBranch.ReceivePackages</c>, and only while
/// the PC is inside a player-faction zone. In other words it already means
/// "put it in the home mailbox, whenever the PC is home" — no matter where the
/// settlement happened. Which is precisely the behaviour we want.
///
/// Why not <c>FactionBranch.PutInMailBox</c> directly (what this mod used to do):
/// it resolves the mailbox through <c>EClass._map</c>, i.e. the map the PC is
/// standing on, and when that finds nothing it does
/// <c>EClass._zone.AddCard(t, GetMailBoxPos())</c>. With the PC out on the
/// world map there is no mailbox, so a dispatch party coming home dropped its
/// parcel on the overworld floor — pickable without entering anything
/// (workshop report 2026-11-02, 依可希娅: coast east of the Exile Village).
/// </summary>
internal static class HomeDelivery
{
    /// <summary>
    /// Queue <paramref name="pack"/> for home delivery. Returns false only when
    /// the queue is unreachable, so the caller can hand it over directly
    /// instead of losing it.
    /// </summary>
    internal static bool Deliver(Thing? pack)
    {
        if (pack == null)
        {
            return false;
        }

        try
        {
            EClass.world.SendPackage(pack);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("home delivery: " + ex.Message);
            return false;
        }
    }
}
