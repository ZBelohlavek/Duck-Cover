using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Launch pads (WP18). A player whose accepted move ends on a pad is in flight for the pad's arc:
    // their moves are then checked against the arc instead of the speed allowance and the rise limit,
    // so the throw isn't flagged as too fast, and leaving the arc is refused like any other bad move.
    // The flight ends when a move comes down at the landing, when the server corrects or places the
    // player, or a little after the arc's time is up.
    public sealed partial class MovementRules
    {
        sealed class Flight
        {
            public int playerId;
            public LaunchPad pad;
            /// <summary>Seconds since the server launched the player.</summary>
            public float t;
        }

        readonly List<Flight> flights = new List<Flight>();

        /// <summary>Is the player flying a launch pad's arc right now?</summary>
        public bool Flying(int playerId) => FlightOf(playerId) != null;

        /// <summary>The pad a player is flying from, or null.</summary>
        public LaunchPad FlightPad(int playerId) => FlightOf(playerId)?.pad;

        Flight FlightOf(int playerId)
        {
            foreach (var f in flights) if (f.playerId == playerId) return f;
            return null;
        }

        void LaunchIfOnPad(PlayerMotion m)
        {
            if (Layout == null || Layout.launchPads == null) return;
            foreach (var pad in Layout.launchPads)
                if (pad != null && pad.Contains(m.position))
                {
                    flights.Add(new Flight { playerId = m.playerId, pad = pad });
                    Publish(new LaunchedEvent { playerId = m.playerId, pad = pad.name });
                    return;
                }
        }

        CommandResult ApplyInFlight(PlayerMotion m, MoveCommand cmd)
        {
            var f = FlightOf(cmd.playerId);
            float off = f.pad.DistanceTo(cmd.position, 0f, f.t + Rules.launchLeadSeconds);
            if (off > Rules.launchTolerance) return Correct(m, "left the launch arc");

            m.position = cmd.position;
            m.velocity = cmd.velocity;
            m.yaw = cmd.yaw;
            m.pitch = Math.Max(-89f, Math.Min(89f, cmd.pitch));
            m.crouched = cmd.crouched;
            m.grounded = cmd.grounded;
            m.sprinting = false;
            m.vaulting = false;
            if (cmd.grounded && Vec3.Distance(cmd.position, f.pad.landing) <= Rules.launchTolerance) Land(cmd.playerId);
            return CommandResult.Accepted;
        }

        /// <summary>End a player's flight, if any, with a full allowance (they arrive running).</summary>
        void Land(int playerId)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
                if (flights[i].playerId == playerId)
                {
                    flights.RemoveAt(i);
                    AllowanceOf(playerId).metres = MaxAllowance(playerId);
                }
        }

        void TickFlights(float dt)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                f.t += dt;
                if (f.t > f.pad.seconds + Rules.launchGraceSeconds || !State.IsAlive(f.playerId)) Land(f.playerId);
            }
        }
    }
}
