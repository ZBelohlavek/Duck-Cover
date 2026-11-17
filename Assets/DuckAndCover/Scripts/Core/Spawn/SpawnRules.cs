using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Eliminations, respawn waits and waves, and where a player comes back. The wait grows with
    /// level (8 s at level 1 to 17 s at level 10 with the plan's numbers), and players return in waves
    /// so teammates who fell together come back together. Driven by <see cref="MatchRules"/>.
    /// </summary>
    public sealed class SpawnRules
    {
        const float Epsilon = 1e-4f;

        readonly MatchState state;
        readonly Ruleset rules;
        readonly IGameEvents events;

        public SpawnRules(MatchState state, Ruleset rules, IGameEvents events)
        {
            this.state = state;
            this.rules = rules;
            this.events = events;
        }

        /// <summary>Respawn wait for a player at a level.</summary>
        public float WaitFor(int level) => rules.respawnBaseSeconds + rules.respawnPerLevelSeconds * Math.Max(0, level - 1);

        /// <summary>
        /// Seconds until a waiting player is back: their own wait, rounded up to the next wave.
        /// 0 for living players.
        /// </summary>
        public float TimeUntilRespawn(int playerId)
        {
            var p = state.Player(playerId);
            if (p == null || p.alive || !p.connected) return 0f;
            float at = p.respawnAt;
            if (rules.respawnWaveSeconds > 0f)
            {
                float wave = state.nextWaveAt;
                while (wave < at - Epsilon) wave += rules.respawnWaveSeconds;
                at = wave;
            }
            return Math.Max(0f, at - state.elapsed);
        }

        /// <summary>Everyone connected appears at their base (start of Deploy).</summary>
        public void SpawnAll()
        {
            foreach (var p in state.players) if (p.connected) SpawnAtBase(p);
            state.nextWaveAt = rules.respawnWaveSeconds;
        }

        /// <summary>One player appears in their spawn room before play (a late joiner during Deploy).</summary>
        public void SpawnAtBase(PlayerState p)
        {
            p.alive = true;
            Publish(new PlayerSpawnedEvent { playerId = p.id, team = p.team, zone = ZoneId.None, initial = true });
        }

        public void Eliminate(PlayerState p, int killerId, bool disconnected)
        {
            p.alive = false;
            p.deaths++;
            p.respawnAt = state.elapsed + WaitFor(p.level);
            var killer = killerId >= 0 ? state.Player(killerId) : null;
            if (killer != null && killer.team != p.team) killer.eliminations++;
            Publish(new PlayerEliminatedEvent
            {
                playerId = p.id,
                team = p.team,
                killerId = killerId,
                disconnected = disconnected,
                respawnAt = p.respawnAt,
            });
        }

        /// <summary>Bring back everyone whose wait is over, at the wave.</summary>
        public void Tick()
        {
            if (rules.respawnWaveSeconds > 0f)
            {
                if (state.elapsed < state.nextWaveAt - Epsilon) return;
                while (state.nextWaveAt <= state.elapsed + Epsilon) state.nextWaveAt += rules.respawnWaveSeconds;
            }
            foreach (var p in state.players)
            {
                if (p.alive || !p.connected || p.team == TeamId.None) continue;
                if (p.respawnAt > state.elapsed + Epsilon) continue;
                p.alive = true;
                var zone = SpawnZoneFor(p);
                Publish(new PlayerSpawnedEvent { playerId = p.id, team = p.team, zone = zone, initial = false });
            }
        }

        /// <summary>Where the player would come back right now: their forward point if it is held and quiet, else base.</summary>
        public ZoneId SpawnZoneFor(PlayerState p)
        {
            if (!p.spawnChoice.IsValid) return ZoneId.None;
            var z = state.Zone(p.spawnChoice);
            if (z == null || !z.active || z.forwardSpawnFor != p.team || z.owner != p.team) return ZoneId.None;
            if (z.contested || z.Count(p.team.Other()) > 0 || z.progress > 0f) return ZoneId.None;
            return z.id;
        }

        /// <summary>Whether a zone can ever be this team's spawn choice.</summary>
        public bool CanChoose(TeamId team, ZoneId zone)
        {
            if (!zone.IsValid) return true;
            var z = state.Zone(zone);
            return z != null && z.forwardSpawnFor == team;
        }

        void Publish(GameEvent e)
        {
            e.tick = state.tick;
            events.Publish(e);
        }
    }
}
