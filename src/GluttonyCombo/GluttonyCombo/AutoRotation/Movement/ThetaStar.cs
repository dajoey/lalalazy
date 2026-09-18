// Ported from awgil/ffxiv_bossmod BossMod/Pathfinding/ThetaStar.cs (BSD-3-Clause,
// Copyright (c) 2022-2024 Andrew Gilewsky). See THIRD_PARTY_NOTICES.md.
// PURE: no Dalamud types; compiled into tests/GluttonyCombo.SmartMoverHarness.

#region

using System;
using System.Collections.Generic;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation.Movement;

/// <summary>
///     Any-angle grid search where g is travel TIME (seconds) and every pixel
///     knows when it becomes lethal. A path is "safe" while its leeway (min over
///     the path of pixel-max-g minus arrival g) stays positive. Nodes are ranked
///     by <see cref="Score"/> first, then f = g + h.
/// </summary>
internal sealed class ThetaStar
{
    public enum Score
    {
        JustBad,           // unsafe path, unsafe destination no better than start
        UltimatelyBetter,  // unsafe path, unsafe destination that activates later than start
        UltimatelySafe,    // unsafe path, safe destination
        UnsafeAsStart,     // unsafe path, destination as bad as start
        SemiSafeAsStart,   // semi-safe path, destination as bad as start
        UnsafeImprove,     // unsafe path, destination better than start
        SemiSafeImprove,   // semi-safe path, destination better than start
        Safe,              // fully safe path to a safe cell
        SafeBetterPrio,    // ... with a goal priority above start
        SafeMaxPrio,       // ... with the max goal priority
    }

    public struct Node
    {
        public float GScore;
        public float HScore;
        public int ParentIndex;
        public int OpenHeapIndex; // -1 closed, 0 unvisited, else heap index + 1
        public float PathLeeway;
        public float PathMinG;
        public Score Score;
        public Vector2 EnterOffset; // from cell centre, +-0.5

        public readonly float FScore => GScore + HScore;
    }

    private NavMap _map = new();
    private Node[] _nodes = Array.Empty<Node>();
    private readonly List<int> _openList = new();
    private float _deltaGSide;
    public int StartNodeIndex { get; private set; }
    private float _startMaxG;
    private float _startPrio;
    private Score _startScore;
    private int _bestIndex;
    private int _fallbackIndex;

    public int NumSteps { get; private set; }
    public int NumReopens { get; private set; }

    private const float BorderCushion = 0.1f;
    private const float MaxNeighbourOffset = 0.5f - BorderCushion;
    private const float CenterToNeighbour = 0.5f + BorderCushion;

    public ref Node NodeByIndex(int index) => ref _nodes[index];
    public Score StartScore => _startScore;
    public float StartMaxG => _startMaxG;

    /// <param name="gMultiplier"> inverse speed: turns grid distance into seconds. </param>
    public void Start(NavMap map, Vector2 startPos, float gMultiplier)
    {
        _map = map;
        var numPixels = map.Width * map.Height;
        if (_nodes.Length < numPixels)
            _nodes = new Node[numPixels];
        else
            Array.Fill(_nodes, default, 0, numPixels);
        _openList.Clear();
        _deltaGSide = map.Resolution * gMultiplier;

        PrefillH();

        var startFrac = map.WorldToGridFrac(startPos);
        var start = map.ClampToGrid(map.FracToGrid(startFrac));
        StartNodeIndex = _bestIndex = _fallbackIndex = _map.GridToIndex(start.x, start.y);
        _startMaxG = _map.PixelMaxG[StartNodeIndex];
        _startPrio = _map.PixelPriority[StartNodeIndex];
        _startScore = CalculateScore(_startMaxG, _startMaxG, _startMaxG, StartNodeIndex);
        NumSteps = NumReopens = 0;

        startFrac.X = Math.Clamp(startFrac.X - (start.x + 0.5f), -0.5f, 0.5f);
        startFrac.Y = Math.Clamp(startFrac.Y - (start.y + 0.5f), -0.5f, 0.5f);
        ref var startNode = ref _nodes[StartNodeIndex];
        startNode = new Node
        {
            GScore = 0,
            HScore = startNode.HScore,
            ParentIndex = StartNodeIndex,
            PathLeeway = _startMaxG,
            PathMinG = _startMaxG,
            Score = _startScore,
            EnterOffset = startFrac,
        };
        AddToOpen(StartNodeIndex);
    }

    public bool ExecuteStep()
    {
        if (_openList.Count == 0)
            return false;

        ++NumSteps;
        var nextNodeIndex = PopMinOpen();
        var (nx, ny) = _map.IndexToGrid(nextNodeIndex);
        ref var nextNode = ref _nodes[nextNodeIndex];

        if (CompareNodeScores(ref nextNode, ref _nodes[_bestIndex]) < 0)
            _bestIndex = nextNodeIndex;
        if (nextNode.Score == Score.UltimatelySafe && (_fallbackIndex == StartNodeIndex || CompareNodeScores(ref nextNode, ref _nodes[_fallbackIndex]) < 0))
            _fallbackIndex = nextNodeIndex;

        if (ny > _map.MinY)
            VisitNeighbour(nx, ny, nextNodeIndex, nx, ny - 1, nextNodeIndex - _map.Width, CenterToNeighbour + nextNode.EnterOffset.Y);
        if (nx > _map.MinX)
            VisitNeighbour(nx, ny, nextNodeIndex, nx - 1, ny, nextNodeIndex - 1, CenterToNeighbour + nextNode.EnterOffset.X);
        if (nx < _map.MaxX)
            VisitNeighbour(nx, ny, nextNodeIndex, nx + 1, ny, nextNodeIndex + 1, CenterToNeighbour - nextNode.EnterOffset.X);
        if (ny < _map.MaxY)
            VisitNeighbour(nx, ny, nextNodeIndex, nx, ny + 1, nextNodeIndex + _map.Width, CenterToNeighbour - nextNode.EnterOffset.Y);
        return true;
    }

    public int Execute(int maxSteps = int.MaxValue)
    {
        while (_nodes[_bestIndex].HScore > 0 && _fallbackIndex == StartNodeIndex && NumSteps < maxSteps && ExecuteStep())
        {
        }
        return BestIndex();
    }

    public int BestIndex()
    {
        if (_nodes[_bestIndex].Score > _startScore)
            return _bestIndex;

        if (_fallbackIndex != StartNodeIndex)
        {
            // walk the fallback's parent chain to the last node at least as good as start
            var destIndex = _fallbackIndex;
            var parentIndex = _nodes[destIndex].ParentIndex;
            while (_nodes[parentIndex].Score < _startScore)
            {
                destIndex = parentIndex;
                parentIndex = _nodes[destIndex].ParentIndex;
            }

            ref var startNode = ref _nodes[parentIndex];
            ref var destNode = ref _nodes[destIndex];
            var (x2, y2) = _map.IndexToGrid(destIndex);
            var (x1, y1) = _map.IndexToGrid(parentIndex);
            var dx = x2 - x1;
            var dy = y2 - y1;
            var sx = dx > 0 ? 1 : -1;
            var sy = dy > 0 ? 1 : -1;
            var hsx = 0.5f * sx;
            var hsy = 0.5f * sy;
            var indexDeltaX = sx;
            var indexDeltaY = sy * _map.Width;

            var ab = new Vector2(dx + destNode.EnterOffset.X - startNode.EnterOffset.X, dy + destNode.EnterOffset.Y - startNode.EnterOffset.Y);
            var abLen = ab.Length();
            if (abLen < 0.0001f)
                return parentIndex;
            ab /= abLen;
            var invx = ab.X != 0 ? 1 / ab.X : float.MaxValue;
            var invy = ab.Y != 0 ? 1 / ab.Y : float.MaxValue;
            var off1 = startNode.EnterOffset;
            while (x1 != x2 || y1 != y2)
            {
                var tx = (hsx - off1.X) * invx;
                var ty = (hsy - off1.Y) * invy;
                if (tx < 0 || x1 == x2)
                    tx = float.MaxValue;
                if (ty < 0 || y1 == y2)
                    ty = float.MaxValue;

                var nextIndex = parentIndex;
                if (tx < ty)
                {
                    x1 += sx;
                    off1.X = -hsx;
                    off1.Y = Math.Clamp(off1.Y + tx * ab.Y, -0.5f, +0.5f);
                    nextIndex += indexDeltaX;
                }
                else
                {
                    y1 += sy;
                    off1.Y = -hsy;
                    off1.X = Math.Clamp(off1.X + ty * ab.X, -0.5f, +0.5f);
                    nextIndex += indexDeltaY;
                }

                if (_nodes[nextIndex].Score < _startScore)
                    return parentIndex;
                parentIndex = nextIndex;
            }
        }

        return _bestIndex;
    }

    public Score CalculateScore(float pixMaxG, float pathMinG, float pathLeeway, int pixelIndex)
    {
        var destSafe = pixMaxG == float.MaxValue;
        var pathSafe = pathLeeway > 0;
        var destBetter = pixMaxG > _startMaxG;
        if (destSafe && pathSafe)
        {
            var prio = _map.PixelPriority[pixelIndex];
            return prio == _map.MaxPriority ? Score.SafeMaxPrio : prio > _startPrio ? Score.SafeBetterPrio : Score.Safe;
        }

        if (pathMinG == _startMaxG)
            return pathSafe
                ? (destBetter ? Score.SemiSafeImprove : Score.SemiSafeAsStart)
                : (destBetter ? Score.UnsafeImprove : Score.UnsafeAsStart);

        return destSafe ? Score.UltimatelySafe : destBetter ? Score.UltimatelyBetter : Score.JustBad;
    }

    public static int CompareNodeScores(ref Node l, ref Node r)
    {
        if (l.Score != r.Score)
            return l.Score > r.Score ? -2 : +2;

        var gl = l.GScore;
        var gr = r.GScore;
        var fl = gl + l.HScore;
        var fr = gr + r.HScore;
        if (fl + 0.00001f < fr)
            return -1;
        if (fr + 0.00001f < fl)
            return +1;
        if (gl != gr)
            return gl > gr ? -1 : 1; // tie-break toward larger g
        return 0;
    }

    private static Vector2 CalculateEnterOffset(int fromX, int fromY, Vector2 fromOff, int toX, int toY)
    {
        var x = fromX == toX ? fromOff.X : fromX < toX ? -MaxNeighbourOffset : +MaxNeighbourOffset;
        var y = fromY == toY ? fromOff.Y : fromY < toY ? -MaxNeighbourOffset : +MaxNeighbourOffset;
        return new Vector2(x, y);
    }

    private float LineOfSight(int x1, int y1, Vector2 off1, int x2, int y2, out Vector2 off2, out float length, out float minG)
    {
        var curNodeIndex = _map.GridToIndex(x1, y1);
        ref var startNode = ref _nodes[curNodeIndex];
        var minLeeway = startNode.PathLeeway;
        minG = startNode.PathMinG;

        var dx = x2 - x1;
        var dy = y2 - y1;
        var sx = dx > 0 ? 1 : -1;
        var sy = dy > 0 ? 1 : -1;
        var hsx = 0.5f * sx;
        var hsy = 0.5f * sy;
        var indexDeltaX = sx;
        var indexDeltaY = sy * _map.Width;

        off2 = CalculateEnterOffset(x1, y1, off1, x2, y2);
        var abx = dx + off2.X - off1.X;
        var aby = dy + off2.Y - off1.Y;
        length = MathF.Sqrt(abx * abx + aby * aby);
        if (length < 0.01f)
            return minLeeway;

        abx /= length;
        aby /= length;
        var invx = abx != 0 ? 1 / abx : float.MaxValue;
        var invy = aby != 0 ? 1 / aby : float.MaxValue;

        var curG = startNode.GScore;
        var prevPixMaxG = _map.PixelMaxG[curNodeIndex];
        var numIterationsLeft = dx * sx + dy * sy;
        var curOffX = off1.X;
        var curOffY = off1.Y;
        while (numIterationsLeft-- > 0)
        {
            var tx = x1 != x2 ? (hsx - curOffX) * invx : float.MaxValue;
            var ty = y1 != y2 ? (hsy - curOffY) * invy : float.MaxValue;
            if (tx < 0)
                tx = float.MaxValue;
            if (ty < 0)
                ty = float.MaxValue;

            if (tx < ty)
            {
                x1 += sx;
                curOffX = -hsx;
                curOffY = Math.Clamp(curOffY + tx * aby, -0.5f, +0.5f);
                curG += tx * _deltaGSide;
                curNodeIndex += indexDeltaX;
            }
            else
            {
                y1 += sy;
                curOffY = -hsy;
                curOffX = Math.Clamp(curOffX + ty * abx, -0.5f, +0.5f);
                curG += ty * _deltaGSide;
                curNodeIndex += indexDeltaY;
            }

            if (curNodeIndex < 0 || curNodeIndex >= _map.Width * _map.Height)
                return float.MinValue;

            var pixG = _map.PixelMaxG[curNodeIndex];
            minLeeway = MathF.Min(minLeeway, MathF.Min(pixG, prevPixMaxG) - curG);
            minG = MathF.Min(minG, pixG);
            prevPixMaxG = pixG;
        }
        return minLeeway;
    }

    private void VisitNeighbour(int parentX, int parentY, int parentIndex, int nodeX, int nodeY, int nodeIndex, float deltaGrid)
    {
        ref var destNode = ref _nodes[nodeIndex];
        if (destNode.OpenHeapIndex < 0 && destNode.Score >= Score.SemiSafeAsStart)
            return; // closed with a decent path

        var destPixG = _map.PixelMaxG[nodeIndex];
        var parentPixG = _map.PixelMaxG[parentIndex];
        if (destPixG < 0 && parentPixG >= 0)
            return; // never enter impassable from passable
        var deltaG = _deltaGSide * deltaGrid;
        var destGScore = _nodes[parentIndex].GScore + deltaG;
        var destLeeway = MathF.Min(_nodes[parentIndex].PathLeeway, MathF.Min(destPixG, parentPixG) - destGScore);
        var destMinG = MathF.Min(_nodes[parentIndex].PathMinG, destPixG);
        var altNode = new Node
        {
            GScore = destGScore,
            HScore = destNode.HScore,
            ParentIndex = parentIndex,
            OpenHeapIndex = destNode.OpenHeapIndex,
            PathLeeway = destLeeway,
            PathMinG = destMinG,
            Score = CalculateScore(destPixG, destMinG, destLeeway, nodeIndex),
            EnterOffset = CalculateEnterOffset(parentX, parentY, _nodes[parentIndex].EnterOffset, nodeX, nodeY),
        };

        // any-angle shortcut from the grandparent
        var grandParentIndex = _nodes[parentIndex].ParentIndex;
        if (_nodes[grandParentIndex].PathMinG >= _nodes[parentIndex].PathMinG)
        {
            var (gpX, gpY) = _map.IndexToGrid(grandParentIndex);
            var losLeeway = LineOfSight(gpX, gpY, _nodes[grandParentIndex].EnterOffset, nodeX, nodeY, out var gpOffset, out var gpDist, out var losMinG);
            var losScore = CalculateScore(destPixG, losMinG, losLeeway, nodeIndex);
            if (losScore > altNode.Score || losScore == altNode.Score && losLeeway >= (losScore >= Score.Safe ? 0 : altNode.PathLeeway))
            {
                altNode.GScore = _nodes[grandParentIndex].GScore + _deltaGSide * gpDist;
                altNode.ParentIndex = grandParentIndex;
                altNode.PathLeeway = losLeeway;
                altNode.PathMinG = losMinG;
                altNode.Score = losScore;
                altNode.EnterOffset = gpOffset;
            }
        }

        var visit = destNode.OpenHeapIndex == 0 || CompareNodeScores(ref altNode, ref destNode) < (destNode.OpenHeapIndex < 0 ? -1 : 0);
        if (visit)
        {
            if (destNode.OpenHeapIndex < 0)
                ++NumReopens;
            destNode = altNode;
            AddToOpen(nodeIndex);
        }
    }

    private void PrefillH()
    {
        var iCell = 0;
        for (var y = 0; y < _map.Height; ++y)
        {
            for (var x = 0; x < _map.Width; ++x, ++iCell)
            {
                if (_map.PixelPriority[iCell] < _map.MaxPriority)
                {
                    ref var node = ref _nodes[iCell];
                    node.HScore = float.MaxValue;
                    if (x > 0)
                        UpdateHNeighbour(x, y, ref node, iCell - 1);
                    if (y > 0)
                        UpdateHNeighbour(x, y, ref node, iCell - _map.Width);
                }
            }
        }
        --iCell;
        for (int y0 = _map.Height - 1, y = y0; y >= 0; --y)
        {
            for (int x0 = _map.Width - 1, x = x0; x >= 0; --x, --iCell)
            {
                if (_map.PixelPriority[iCell] < _map.MaxPriority)
                {
                    ref var node = ref _nodes[iCell];
                    if (x < x0)
                        UpdateHNeighbour(x, y, ref node, iCell + 1);
                    if (y < y0)
                        UpdateHNeighbour(x, y, ref node, iCell + _map.Width);
                }
            }
        }
    }

    private void UpdateHNeighbour(int x1, int y1, ref Node node, int neighIndex)
    {
        ref var neighbour = ref _nodes[neighIndex];
        if (neighbour.HScore == 0)
        {
            node.HScore = _deltaGSide;
            node.ParentIndex = neighIndex;
        }
        else if (neighbour.HScore < float.MaxValue)
        {
            var (x2, y2) = _map.IndexToGrid(neighbour.ParentIndex);
            var dx = x2 - x1;
            var dy = y2 - y1;
            var hScore = _deltaGSide * MathF.Sqrt(dx * dx + dy * dy);
            if (hScore < node.HScore)
            {
                node.HScore = hScore;
                node.ParentIndex = neighbour.ParentIndex;
            }
        }
    }

    private void AddToOpen(int nodeIndex)
    {
        if (_nodes[nodeIndex].OpenHeapIndex <= 0)
        {
            _openList.Add(nodeIndex);
            _nodes[nodeIndex].OpenHeapIndex = _openList.Count;
        }
        PercolateUp(_nodes[nodeIndex].OpenHeapIndex - 1);
    }

    private int PopMinOpen()
    {
        var nodeIndex = _openList[0];
        _openList[0] = _openList[^1];
        _nodes[nodeIndex].OpenHeapIndex = -1;
        _openList.RemoveAt(_openList.Count - 1);
        if (_openList.Count > 0)
        {
            _nodes[_openList[0]].OpenHeapIndex = 1;
            PercolateDown(0);
        }
        return nodeIndex;
    }

    private void PercolateUp(int heapIndex)
    {
        var nodeIndex = _openList[heapIndex];
        ref var node = ref _nodes[nodeIndex];
        while (heapIndex > 0)
        {
            var parentHeapIndex = (heapIndex - 1) >> 1;
            var parentNodeIndex = _openList[parentHeapIndex];
            ref var parent = ref _nodes[parentNodeIndex];
            if (CompareNodeScores(ref node, ref parent) >= 0)
                break;
            _openList[heapIndex] = parentNodeIndex;
            parent.OpenHeapIndex = heapIndex + 1;
            heapIndex = parentHeapIndex;
        }
        _openList[heapIndex] = nodeIndex;
        node.OpenHeapIndex = heapIndex + 1;
    }

    private void PercolateDown(int heapIndex)
    {
        var nodeIndex = _openList[heapIndex];
        ref var node = ref _nodes[nodeIndex];
        var maxSize = _openList.Count;
        while (true)
        {
            var childHeapIndex = (heapIndex << 1) + 1;
            if (childHeapIndex >= maxSize)
                break;
            var childNodeIndex = _openList[childHeapIndex];
            var altChildHeapIndex = childHeapIndex + 1;
            if (altChildHeapIndex < maxSize)
            {
                var altChildNodeIndex = _openList[altChildHeapIndex];
                if (CompareNodeScores(ref _nodes[altChildNodeIndex], ref _nodes[childNodeIndex]) < 0)
                {
                    childHeapIndex = altChildHeapIndex;
                    childNodeIndex = altChildNodeIndex;
                }
            }
            if (CompareNodeScores(ref node, ref _nodes[childNodeIndex]) < 0)
                break;
            _openList[heapIndex] = childNodeIndex;
            _nodes[childNodeIndex].OpenHeapIndex = heapIndex + 1;
            heapIndex = childHeapIndex;
        }
        _openList[heapIndex] = nodeIndex;
        node.OpenHeapIndex = heapIndex + 1;
    }
}
