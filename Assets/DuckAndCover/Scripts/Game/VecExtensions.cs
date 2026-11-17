using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>Converts between Core's engine-free <see cref="Vec3"/> and Unity's Vector3.</summary>
    public static class VecExtensions
    {
        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Vector3 ToVector3(this Vec3 v) => new Vector3(v.x, v.y, v.z);
    }
}
