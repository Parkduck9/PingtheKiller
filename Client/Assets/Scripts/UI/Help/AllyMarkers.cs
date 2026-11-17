using System;
using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 4.6 (11/17 R3): the Survivor HUD's marker for every Lagging ally. On screen: a world tag above the body,
    // `Mina · Lagging · 23 m`. Off screen: an edge arrow (the Killer HUD's Lagging chevron, so one look means one thing) with
    // the same tag beside it. Placement is the shared edge rule (EdgePlacement: safe rectangle inset 24 u, cluster keep-out
    // expanded by 8 u) and EdgeSpread keeps no two edge footprints overlapping. Positions are panel units, y down. Pure
    // geometry here; Draw hands the result to a MarkerLayer. Survivors receive every Survivor entry (HUD_SPEC 0.1), so this
    // adds no information the client does not already have.
    public sealed class AllyMarkers
    {
        public const float TagPointHeight = 1.2f, TagGap = 4, MaxMarkers = 7;

        public struct Mark
        {
            public int Id;
            public bool OnScreen;       // false = edge arrow
            public Vector2 At;          // on screen: the tag anchor (below the tag); edge: the arrow centre
            public Vector2 Pointing;    // edge only, y down
            public string Text;
            public float Distance;
            public bool TagShown;       // edge only: false when the edge was too full for the tag (the arrow stays)
            public Rect Tag;            // edge only: the tag rectangle at the spread position
        }

        private readonly List<Mark> marks = new List<Mark>();
        private readonly List<int> spreadIndex = new List<int>();
        private readonly EdgeSpread spread = new EdgeSpread();
        private readonly List<Mate> lagging = new List<Mate>();

        public IReadOnlyList<Mark> Marks => marks;
        public int OnScreenCount { get; private set; }
        public int EdgeCount { get; private set; }

        // names: lobby nickname by player id (ellipsized to 12 characters like every prompt). Mates not Lagging are ignored.
        public void Build(float ownX, float ownZ, IReadOnlyList<Mate> mates, Func<int, string> names, ViewPose pose, Vector2 screen, Rect cluster)
        {
            marks.Clear(); spreadIndex.Clear(); lagging.Clear();
            OnScreenCount = EdgeCount = 0;
            if (mates == null) return;
            foreach (Mate mate in mates) if (mate.Status == PlayerStatus.Lagging) lagging.Add(mate);
            // The nearest first, so a crowded frame keeps the closest allies (at most MaxMarkers).
            lagging.Sort((a, b) => Sq(a.X - ownX, a.Z - ownZ).CompareTo(Sq(b.X - ownX, b.Z - ownZ)));
            Rect safe = EdgePlacement.SafeRect(screen, false), keepOut = EdgePlacement.KeepOut(cluster);
            spread.Begin(safe, keepOut);
            for (int i = 0; i < lagging.Count && i < MaxMarkers; i++)
            {
                Mate mate = lagging[i];
                float distance = Mathf.Sqrt(Sq(mate.X - ownX, mate.Z - ownZ));
                string text = HelpCopy.AllyTag(SurvivorReadout.Ellipsize(names?.Invoke(mate.Id)), distance);
                var point = new Vector3(mate.X, TagPointHeight, mate.Z);
                if (pose.ToViewport(point, out Vector2 v) && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1)
                {
                    var p = new Vector2(v.x * screen.x, (1 - v.y) * screen.y);
                    p = EdgePlacement.ClampOutside(p, keepOut, safe);
                    marks.Add(new Mark { Id = mate.Id, OnScreen = true, At = p, Text = text, Distance = distance, TagShown = true });
                    spreadIndex.Add(-1);
                    OnScreenCount++;
                    continue;
                }
                Vector2 edge = EdgePlacement.Place(screen, EdgePlacement.Direction(pose.ToCamera(point)), safe, keepOut, true, out Vector2 pointing);
                float half = MarkerLayer.ChevronHalf(true);
                int item = spread.Add(edge, new Vector2(half, half), MarkerLayer.TagSize(text, TagStyle.Text), EdgeTagMode.Beside, LayoutPriority.Edge, mate.Id, distance);
                marks.Add(new Mark { Id = mate.Id, OnScreen = false, At = edge, Pointing = pointing, Text = text, Distance = distance });
                spreadIndex.Add(item);
                EdgeCount++;
            }
            spread.Solve();
            for (int i = 0; i < marks.Count; i++)
            {
                if (spreadIndex[i] < 0) continue;
                Mark mark = marks[i];
                mark.At = spread.Glyph(spreadIndex[i]);
                mark.TagShown = spread.TagShown(spreadIndex[i]);
                mark.Tag = spread.Tag(spreadIndex[i]);
                marks[i] = mark;
            }
        }

        // Replaces the layer's markers with this frame's: world tags above the bodies, then arrows with their tags; one layout
        // pass keeps every tag clear of the others and of the cluster.
        public void Draw(MarkerLayer layer, Rect safe, Rect keepOut)
        {
            layer.Clear(MarkerLayer.Static);
            layer.Clear(MarkerLayer.Signals);
            foreach (Mark mark in marks)
            {
                if (mark.OnScreen)
                {
                    layer.Tag(MarkerLayer.Signals, mark.At - new Vector2(0, TagGap), mark.Text, TagStyle.Lagging, TagPivot.Above, LayoutPriority.Survivor, false);
                    continue;
                }
                layer.Chevron(MarkerLayer.Signals, mark.At, mark.Pointing, true);
                if (mark.TagShown) layer.Tag(MarkerLayer.Signals, mark.Tag.center, mark.Text, TagStyle.Text, TagPivot.Center, LayoutPriority.Edge, true);
            }
            layer.Layout(safe, keepOut);
            layer.Commit(MarkerLayer.Static);
            layer.Commit(MarkerLayer.Signals);
        }

        private static float Sq(float dx, float dz) => dx * dx + dz * dz;
    }
}
