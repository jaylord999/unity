using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// The hand-built part of the map, as seen by the procedural passes. <see cref="TownAnchor"/>
    /// implements it at runtime; keeping it an interface means the world maths can also be
    /// exercised without a Unity scene (tooling, tests, batch checks).
    /// </summary>
    public interface ILegacyMap
    {
        /// <summary>True when there is a hand-built map to work around.</summary>
        bool HasLegacy { get; }

        /// <summary>Bottom-left corner of the hand-built rectangle (world XZ).</summary>
        Vector2 Origin { get; }

        /// <summary>Size of the hand-built rectangle (metres).</summary>
        Vector2 Size { get; }

        /// <summary>Print a warning when a road exit does not sit on the chunk grid.</summary>
        bool LogAlignment { get; }

        void EnsureReady();

        bool InsideLegacy(float x, float z);

        /// <summary>Distance from the rectangle: 0 inside, positive outside (huge when absent).</summary>
        float OutsideDistance(float x, float z);

        /// <summary>Height of the hand-built ground projected to the nearest edge point.</summary>
        float EdgeHeight(float x, float z);

        /// <summary>Does the given rectangle touch the hand-built map?</summary>
        bool OverlapsLegacy(float x0, float z0, float x1, float z1, float margin);

        /// <summary>Outward unit direction at a road exit.</summary>
        Vector2 OutsideNormal(Vector2 exit);

        /// <summary>Every road exit that must connect to the procedural road network.</summary>
        Vector2[] AllExits();
    }
}
