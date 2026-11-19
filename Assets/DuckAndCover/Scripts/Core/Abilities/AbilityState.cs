using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>Something an ability placed in the world, as it is right now.</summary>
    [Serializable]
    public class ObjectState
    {
        /// <summary>Unique in the match. Also the deviceId the world reports when a ray hits it.</summary>
        public int id;
        public string defId = "";
        public int ownerId = -1;
        public TeamId team = TeamId.None;
        /// <summary>The ability that placed it.</summary>
        public string abilityId = "";
        public Vec3 position;
        /// <summary>Level direction the object faces (the caster's aim when placed). A wall stands across it.</summary>
        public Vec3 facing = Vec3.Forward;
        public float health;
        /// <summary>Seconds left, when the definition has a lifetime.</summary>
        public float timeLeft;
        public float armLeft;
        public float pulseIn;
        /// <summary>Its numbers as placed, with the owner's talents at that moment applied.</summary>
        public ObjectDef def = new ObjectDef();
        public float radius;
        /// <summary>Seconds since it was placed.</summary>
        public float age;
        /// <summary>
        /// The player or unit it moves with each tick (<see cref="ObjectDef.followAhead"/> and
        /// <see cref="ObjectDef.followRise"/> from their feet, facing their way), or -1 to stay put.
        /// Placing an object with <see cref="ObjectDef.followOwner"/> sets it to the owner; a rule
        /// can point it at someone else. It stops following (stays put) if they go down.
        /// </summary>
        public int followId = -1;

        /// <summary>Its wall width right now: walls that open out (<see cref="ObjectDef.expandTime"/>) grow to full width.</summary>
        public float Width
        {
            get
            {
                if (def == null) return 0f;
                if (def.expandTime <= 0f || age >= def.expandTime) return def.width;
                float from = Math.Max(0f, Math.Min(1f, def.expandFrom));
                return def.width * (from + (1f - from) * Math.Max(0f, age) / def.expandTime);
            }
        }

        public ObjectState Clone()
        {
            var c = (ObjectState)MemberwiseClone();
            c.def = def.Clone();
            return c;
        }
    }

    /// <summary>One place an ability landed and who it caught there.</summary>
    [Serializable]
    public class EffectArea
    {
        public Vec3 point;
        public int[] targets = new int[0];
        /// <summary>Spawned objects caught (only damage reaches them).</summary>
        public int[] objects = new int[0];

        public EffectArea Clone() => new EffectArea { point = point, targets = (int[])targets.Clone(), objects = (int[])objects.Clone() };
    }

    /// <summary>An effect waiting out its delay (a fuse).</summary>
    [Serializable]
    public class PendingEffect
    {
        public float timeLeft;
        public int sourceId = -1;
        public TeamId team = TeamId.None;
        public string abilityId = "";
        public Vec3 aim;
        public EffectDef effect = new EffectDef();
        public List<EffectArea> areas = new List<EffectArea>();
        /// <summary>Area abilities look again for who is there when the delay ends.</summary>
        public bool requery;
        public float radius;
        public string affects = Affects.Enemies;
        public int maxTargets;

        public PendingEffect Clone()
        {
            var c = (PendingEffect)MemberwiseClone();
            c.effect = effect.Clone();
            c.areas = areas.ConvertAll(a => a.Clone());
            return c;
        }
    }

    /// <summary>
    /// Everything the Combat and Abilities rules know: each hero's unit, the objects in the world and
    /// the effects waiting on a delay. Plain data, copied into snapshots.
    /// </summary>
    [Serializable]
    public class AbilityState
    {
        public List<HeroUnit> units = new List<HeroUnit>();
        public List<ObjectState> objects = new List<ObjectState>();
        public List<PendingEffect> pending = new List<PendingEffect>();
        public int nextObjectId;

        public HeroUnit Unit(int playerId)
        {
            foreach (var u in units) if (u.playerId == playerId) return u;
            return null;
        }

        public ObjectState Object(int id)
        {
            foreach (var o in objects) if (o.id == id) return o;
            return null;
        }

        public AbilityState Clone() => new AbilityState
        {
            units = units.ConvertAll(u => u.Clone()),
            objects = objects.ConvertAll(o => o.Clone()),
            pending = pending.ConvertAll(p => p.Clone()),
            nextObjectId = nextObjectId,
        };
    }
}
