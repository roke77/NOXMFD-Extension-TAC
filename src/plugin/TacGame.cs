using System.Collections.Generic;
using NuclearOption.Networking;

namespace TacModule
{
    // The game reads TAC needs beyond NOXMFD's API (docs/tac-plan.md): the local pilot's locked
    // enemies (PULL), whether a target still exists (destroyed targets leave the board, decision
    // 10) and whether a reported unit is an enemy. Same calls NOXMFD's own TGT/target.select use.
    // Main thread only.
    internal static class TacGame
    {
        internal readonly struct Lock
        {
            internal Lock(uint id, string name) { Id = id; Name = name; }
            internal uint Id { get; }
            internal string Name { get; }
        }

        // The enemy units in the local pilot's target list (decision 17), in lock order.
        internal static List<Lock> LockedEnemies()
        {
            var result = new List<Lock>();
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null || ac.NetworkHQ == null) return result;
            List<Unit> targets = ac.weaponManager.GetTargetList();
            if (targets == null) return result;
            foreach (Unit u in targets)
                if (IsEnemyOf(u, ac.NetworkHQ)) result.Add(new Lock(u.persistentID.Id, u.definition?.unitName ?? "?"));
            return result;
        }

        // Destroyed, or otherwise no longer a live unit — the same test target.select applies. Safe
        // as a "destroyed" signal because every client holds every networked unit until it dies: the
        // game adds no Mirage NetworkVisibility culling, so a failed lookup never means "far away".
        // Re-check this if a game update adds interest management.
        internal static bool IsGone(uint id) =>
            !UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) || u == null || u.disabled;

        // An enemy of the local faction, as this client's own game sees it: a leader's REPORT is
        // checked here rather than trusted.
        internal static bool IsEnemy(uint id) =>
            GameManager.GetLocalHQ(out FactionHQ hq) && hq != null
            && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) && IsEnemyOf(u, hq);

        // A live unit with a faction other than ours; no-faction (neutral) units are not enemies.
        private static bool IsEnemyOf(Unit? u, FactionHQ ours) =>
            u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != ours;
    }
}
