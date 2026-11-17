using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 15.1 (Q5): the Shutter and Rail prefabs, built by Editor/Setup/ChasePropSetup (-executeMethod
    // PingTheKiller.Editor.Setup.ChasePropSetup.Run) into Assets/Art/ChaseProps and catalogued in Resources/ChaseProps.asset.
    // A sibling of the WorldArt catalog: it never touches WorldArt, so WorldArt.Matches is unaffected. Like WorldArt, each
    // piece records the Shared box it was fitted to; a catalog built for other WorldChaseProps data is never drawn
    // (ChasePropsView falls back to primitives built from the same ChasePropShapes).
    public sealed class ChasePropArt : ScriptableObject
    {
        [Serializable]
        public struct Piece
        {
            // Origin at the box centre on the floor, already turned onto the prop's axis. Shutter children: Frame, Up,
            // Down, Broken; rail children: Rail, Marker.
            public GameObject Prefab;
            // The WorldChaseProps box: x = MinX, y = MinZ, z = MaxX, w = MaxZ.
            public Vector4 Box;
        }

        public Piece[] Shutters = new Piece[0];
        public Piece[] Rails = new Piece[0];

        public const string ResourceName = "ChaseProps";
        public const float FitTolerance = .01f;

        public static ChasePropArt Load() => Resources.Load<ChasePropArt>(ResourceName);

        public bool Matches() => Matches(WorldChaseProps.Shutters, WorldChaseProps.Rails);

        public bool Matches(IReadOnlyList<ChaseProp> shutters, IReadOnlyList<ChaseProp> rails)
            => Fits(Shutters, shutters) && Fits(Rails, rails);

        private static bool Fits(Piece[] pieces, IReadOnlyList<ChaseProp> boxes)
        {
            if (pieces == null || pieces.Length != boxes.Count) return false;
            for (int i = 0; i < pieces.Length; i++)
            {
                ChaseProp box = boxes[i];
                Vector4 b = pieces[i].Box;
                if (pieces[i].Prefab == null) return false;
                if (Mathf.Abs(b.x - box.MinX) > FitTolerance || Mathf.Abs(b.y - box.MinZ) > FitTolerance
                    || Mathf.Abs(b.z - box.MaxX) > FitTolerance || Mathf.Abs(b.w - box.MaxZ) > FitTolerance) return false;
            }
            return true;
        }
    }
}
