using System;
using DuckAndCover.Core;

/// <summary>Hitbox shapes, the pellet pattern and lag compensation history (WP4).</summary>
static class HitboxTests
{
    static readonly Ruleset R = Ruleset.Default;

    static int PartHit(BodyPose pose, float height, out float distance)
    {
        // A ray along +z at a height, from 10 m in front of the body.
        Vec3 origin = pose.position + new Vec3(0f, height, -10f);
        Hitboxes.Raycast(R, pose, origin, Vec3.Forward, 100f, out distance, out int part);
        return part;
    }

    [Test]
    static void EachRegionAnswersAtItsHeight()
    {
        var pose = new BodyPose(new Vec3(3f, 0f, 5f), false);
        Check.Equal(BodyParts.Head, PartHit(pose, R.headHeight, out float dHead), "head height hits the head");
        Check.True(dHead <= 10f - R.headRadius + 1e-3f && dHead > 9.5f, "at the front of the body (" + dHead + ")");
        Check.Equal(BodyParts.Body, PartHit(pose, R.headHeight - R.headRadius - 0.02f, out _), "just under the head is the shoulders");
        Check.Equal(BodyParts.Body, PartHit(pose, 1.1f, out float dBody), "chest height hits the torso");
        Check.Near(10f - R.torsoRadius, dBody, "the torso is entered at its front", 1e-3f);
        Check.Equal(BodyParts.Legs, PartHit(pose, 0.4f, out _), "knee height hits the legs");
        Check.Equal(BodyParts.None, PartHit(pose, R.headHeight + R.headRadius + 0.05f, out _), "just over the head misses");
        Check.Equal(BodyParts.None, PartHit(new BodyPose(pose.position, false, alive: false), 1.1f, out _), "the dead have no hitboxes");
    }

    [Test]
    static void CrouchingLowersTheHead()
    {
        var pose = new BodyPose(Vec3.Zero, true);
        Check.Equal(BodyParts.None, PartHit(pose, R.headHeight, out _), "a standing head shot goes over a crouched player");
        float crouchHead = R.headHeight * R.crouchHeight / R.standHeight;
        Check.Equal(BodyParts.Head, PartHit(pose, crouchHead, out _), "the crouched head is lower");
    }

    [Test]
    static void SideAndGrazingShots()
    {
        var pose = new BodyPose(Vec3.Zero, false);
        Vec3 o = new Vec3(-10f, 1.1f, R.torsoRadius - 0.01f);
        Check.True(Hitboxes.Raycast(R, pose, o, Vec3.Right, 100f, out _, out int part) && part == BodyParts.Body, "a shot grazing the torso edge hits");
        o = new Vec3(-10f, 1.1f, R.torsoRadius + 0.01f);
        Check.False(Hitboxes.Raycast(R, pose, o, Vec3.Right, 100f, out _, out _), "just outside the edge misses");
        Check.False(Hitboxes.Raycast(R, pose, new Vec3(0f, 1.1f, -10f), Vec3.Forward, 5f, out _, out _), "out of range misses");
        Check.False(Hitboxes.Raycast(R, pose, new Vec3(0f, 1.1f, 10f), Vec3.Forward, 100f, out _, out _), "a body behind the ray isn't hit");
        Vec3 eye = new Vec3(0f, R.eyeHeight, 0f);
        Check.False(Hitboxes.Raycast(R, pose, eye, Vec3.Forward, 100f, out _, out _), "a player's own eyes are inside their head and don't hit it");
        Check.True(Hitboxes.Raycast(R, pose, new Vec3(0f, 5f, 0f), -Vec3.Up, 10f, out float d, out part) && part == BodyParts.Head, "from above hits the head first");
        Check.Near(5f - R.headHeight - R.headRadius, d, "on its crown", 1e-3f);
        Check.True(Hitboxes.Raycast(R, pose, new Vec3(0f, -3f, 0f), Vec3.Up, 10f, out _, out part) && part == BodyParts.Head, "a shot up through the whole body counts as the head");
    }

    [Test]
    static void PelletPatternIsFixedAndInsideTheCone()
    {
        Vec3 fwd = new Vec3(0.3f, 0.1f, 1f).Normalized;
        for (int pellets = 1; pellets <= 12; pellets++)
            for (int i = 0; i < pellets; i++)
            {
                var d = SpreadPattern.Direction(fwd, i, pellets, 5f);
                Check.Near(1f, d.Magnitude, "pellet directions are unit length", 1e-4f);
                double angle = Math.Acos(Math.Min(1.0, Vec3.Dot(d, fwd))) * 180.0 / Math.PI;
                Check.True(angle <= 5.001, "pellet " + i + "/" + pellets + " inside the cone (" + angle + ")");
                Check.True(Vec3.Approximately(d, SpreadPattern.Direction(fwd, i, pellets, 5f)), "the same every time");
            }
        Check.True(Vec3.Approximately(fwd, SpreadPattern.Direction(fwd, 0, 9, 5f)), "pellet 0 goes straight");
        Check.True(Vec3.Approximately(Vec3.Up, SpreadPattern.Direction(Vec3.Up, 0, 9, 5f)), "straight up works too");
        Check.False(Vec3.Approximately(SpreadPattern.Direction(fwd, 1, 9, 5f), SpreadPattern.Direction(fwd, 2, 9, 5f)), "pellets spread out");
    }

    [Test]
    static void HistoryRewindsAndInterpolates()
    {
        var h = new HitHistory(R, 0.05f);
        for (int tick = 1; tick <= 30; tick++) h.Record(7, tick, new BodyPose(new Vec3(tick, 0f, 0f), tick > 20));
        Check.True(h.PoseAt(7, 30, out var now) && now.position.x == 30f, "now is the newest sample");
        Check.True(h.PoseAt(7, 25, out var past) && past.position.x == 25f, "a past tick is where they were");
        Check.True(past.crouched, "with the stance they had");
        Check.True(h.PoseAt(7, 99, out var future) && future.position.x == 30f, "a future tick clamps to the newest");
        int oldest = 30 - h.Capacity + 1;
        Check.True(h.PoseAt(7, 1, out var ancient) && ancient.position.x == oldest, "older than kept clamps to the oldest kept (" + oldest + ")");
        Check.Equal(12, h.Capacity, "half a second at 20 ticks a second, plus two");
        Check.False(h.PoseAt(8, 25, out _), "unknown players have no pose");

        var g = new HitHistory(R, 0.05f);
        g.Record(1, 10, new BodyPose(new Vec3(0f, 0f, 0f), false));
        g.Record(1, 14, new BodyPose(new Vec3(4f, 0f, 0f), false));
        Check.True(g.PoseAt(1, 11, out var mid) && Math.Abs(mid.position.x - 1f) < 1e-4f, "between samples is interpolated");
    }

    [Test]
    static void HistoryRaycastPicksTheNearestBody()
    {
        var h = new HitHistory(R, 0.05f);
        h.Record(1, 5, new BodyPose(new Vec3(0f, 0f, 0f), false));  // shooter
        h.Record(2, 5, new BodyPose(new Vec3(0f, 0f, 10f), false));
        h.Record(3, 5, new BodyPose(new Vec3(0f, 0f, 5f), false));
        Vec3 eye = new Vec3(0f, 1.1f, 0f);
        Check.True(h.Raycast(eye, Vec3.Forward, 100f, 5, 1, out var hit), "hits someone");
        Check.Equal(3, hit.playerId, "the nearer body shields the farther one");
        Check.Equal(BodyParts.Body, hit.bodyPart, "in the torso");
        Check.False(h.Raycast(eye, -Vec3.Forward, 100f, 5, 1, out _), "nothing behind");
    }
}
