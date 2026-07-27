using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Grows mechanoid lattice terrain outward from a cerebrex core when fed steel, and flips the
/// map's world tile to Alpha Biomes' mechanoid-intrusion biome once enough of the map converts.
/// Inert entirely unless <see cref="CerebrexLatticeDefs.Active"/> (Alpha Biomes present).
///
/// Cadence is driven by local counters incremented once per real <see cref="MapComponentTick"/>
/// call, not by <c>Find.TickManager.TicksGame</c> - that keeps <see cref="DebugAdvance"/> able to
/// simulate many ticks synchronously (for pacing/perf tuning via repl_eval) without depending on
/// the real game clock advancing.
/// </summary>
public class MapComponent_CerebrexLattice : MapComponent
{
    private const int ReclassifyIntervalTicks = 15000;
    private const int FlipCheckIntervalTicks = 2000;
    private const int SteelDrawIntervalTicks = 500;
    private const int MaxPlacementsPerTick = 4;
    private const int MaxWalkerStepsPerTick = 16;
    private const int MaxWalkerStepsTotal = 4000;
    private const float BirthRingOffset = 3f;
    private const float KillRingOffset = 15f;
    private const float FrontLeadFactor = 1.15f;
    private const float MinClusterRadius = 10f;
    private const int ThickenSampleK = 6;
    private const float PureRandomFrontierChance = 0.15f;
    private const int BlockSize = 16;

    // ---- persisted ----
    private bool feeding;
    private int cellsConverted;
    private float cellBudget;
    private float cellCredits;
    private IntVec3 seedCell = IntVec3.Invalid;
    private bool flipped;
    private int flippedTick = -1;
    private int flippedOnTileId = -1;

    // ---- rebuilt on FinalizeInit / periodic reclassification, never scribed ----
    private int convertibleCount;
    private float maxR;
    private readonly List<int> frontierList = new();
    private readonly HashSet<int> frontierSet = new();
    private int blocksX;
    private int blocksZ;
    private int[] blockConvertedCounts;

    // ---- transient per-session state, harmless to reset on load ----
    private IntVec3 walkerPos = IntVec3.Invalid;
    private int walkerSteps;
    private bool feedingPaused;
    private int ticksSinceReclassify;
    private int ticksSinceFlipCheck;
    private int ticksSinceSteelDraw;

    public MapComponent_CerebrexLattice(Map map) : base(map)
    {
    }

    public bool Feeding => feeding;
    public int CellsConverted => cellsConverted;
    public bool BiomeFlipped => flipped;
    public float Coverage => convertibleCount > 0 ? cellsConverted / (float)convertibleCount : 0f;

    /// <summary>Steel already drawn from stockpiles and held as credit, waiting to be spent as
    /// cells convert (see <see cref="DrawSteel"/>/<see cref="AdvanceOneTick"/>).</summary>
    public float SteelLoaded => cellCredits;

    public void ToggleFeeding() => feeding = !feeding;

    /// <summary>
    /// Called from CompCerebrexCore.PostSpawnSetup. Only the first core this map ever sees seeds
    /// the lattice - if the core is destroyed and rebuilt elsewhere, re-seeding would reset
    /// maxR/seedCell and produce a discontinuous jump in the DLA radius cap mid-run.
    /// </summary>
    public void Notify_CoreSpawned(Thing core)
    {
        if (seedCell.IsValid)
        {
            return;
        }

        seedCell = core.Position;
        RecomputeMaxR();

        foreach (IntVec3 c in core.OccupiedRect().Cells)
        {
            TryConvertCell(c);
        }
    }

    /// <summary>
    /// Fraction of cells converted within an axis-aligned (radius*2+1) square around <paramref name="pos"/>,
    /// approximated from the 16x16 block density grid rather than a live per-cell scan - reads through
    /// walls by design (no GenSight call), matching the "being around" mood requirement even indoors
    /// where player floors keep the ground itself unconverted.
    /// </summary>
    public float ConvertedFractionNear(IntVec3 pos, int radius)
    {
        if (convertibleCount <= 0 || blockConvertedCounts == null)
        {
            return 0f;
        }

        int minX = pos.x - radius;
        int maxX = pos.x + radius;
        int minZ = pos.z - radius;
        int maxZ = pos.z + radius;

        int minBx = Mathf.Max(0, minX) / BlockSize;
        int maxBx = Mathf.Min(map.Size.x - 1, maxX) / BlockSize;
        int minBz = Mathf.Max(0, minZ) / BlockSize;
        int maxBz = Mathf.Min(map.Size.z - 1, maxZ) / BlockSize;

        float weightedConverted = 0f;
        float totalCells = 0f;

        for (int bx = minBx; bx <= maxBx; bx++)
        {
            for (int bz = minBz; bz <= maxBz; bz++)
            {
                int blockMinX = bx * BlockSize;
                int blockMinZ = bz * BlockSize;
                int blockMaxX = Mathf.Min(blockMinX + BlockSize, map.Size.x) - 1;
                int blockMaxZ = Mathf.Min(blockMinZ + BlockSize, map.Size.z) - 1;

                int overlapMinX = Mathf.Max(minX, blockMinX);
                int overlapMaxX = Mathf.Min(maxX, blockMaxX);
                int overlapMinZ = Mathf.Max(minZ, blockMinZ);
                int overlapMaxZ = Mathf.Min(maxZ, blockMaxZ);

                int overlapW = overlapMaxX - overlapMinX + 1;
                int overlapH = overlapMaxZ - overlapMinZ + 1;
                if (overlapW <= 0 || overlapH <= 0)
                {
                    continue;
                }

                int overlapCells = overlapW * overlapH;
                int blockCells = (blockMaxX - blockMinX + 1) * (blockMaxZ - blockMinZ + 1);
                int blockConverted = blockConvertedCounts[BlockIndex(bx, bz)];

                weightedConverted += blockConverted * (overlapCells / (float)blockCells);
                totalCells += overlapCells;
            }
        }

        return totalCells > 0f ? Mathf.Clamp01(weightedConverted / totalCells) : 0f;
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        if (!CerebrexLatticeDefs.Active)
        {
            return;
        }

        InitBlockGrid();
        RecomputeMaxR();
        Reclassify();

        if (!flipped)
        {
            return;
        }

        if (flippedOnTileId == map.Tile.tileId)
        {
            if (map.Biome != CerebrexLatticeDefs.TargetBiome)
            {
                // Self-heal: BiomeDef shortHash collision reverted the tile to its layer default on
                // load, or Alpha Biomes was only just reinstalled. Re-apply rather than leaving the
                // save silently mismatched with our own "flipped" flag.
                ModLog.Warn($"Cerebrex lattice: {map} biome did not match the expected post-flip biome on load - re-applying.");
                ApplyBiomeFlip(alreadyFlipped: true);
            }
        }
        else
        {
            // Gravship relocation: the same Map object landed on a different world tile. The
            // converted terrain travels with it, but the new tile's biome has not been earned yet.
            ModLog.Log($"Cerebrex lattice: {map} relocated to a new world tile - biome flip must be re-earned here.");
            flipped = false;
            flippedTick = -1;
            flippedOnTileId = -1;
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref feeding, "cfpLatticeFeeding", false);
        Scribe_Values.Look(ref cellsConverted, "cfpLatticeCells", 0);
        Scribe_Values.Look(ref cellBudget, "cfpLatticeBudget", 0f);
        Scribe_Values.Look(ref cellCredits, "cfpLatticeCredits", 0f);
        Scribe_Values.Look(ref seedCell, "cfpLatticeSeed", IntVec3.Invalid);
        Scribe_Values.Look(ref flipped, "cfpLatticeFlipped", false);
        Scribe_Values.Look(ref flippedTick, "cfpLatticeFlippedTick", -1);
        Scribe_Values.Look(ref flippedOnTileId, "cfpLatticeFlippedOnTile", -1);
    }

    public override void MapComponentTick()
    {
        if (!CerebrexLatticeDefs.Active)
        {
            return;
        }

        Settings settings = CerebrexFlavourPackMod.settings;
        if (settings == null || !settings.latticeEnabled)
        {
            return;
        }

        Tick(settings);
    }

    /// <summary>Dev-only fast-forward: simulates <paramref name="ticks"/> ticks synchronously, for
    /// tuning pacing/DLA-blend via repl_eval in seconds instead of waiting on real playtests.</summary>
    public void DebugAdvance(int ticks)
    {
        if (!CerebrexLatticeDefs.Active)
        {
            return;
        }

        Settings settings = CerebrexFlavourPackMod.settings;
        if (settings == null)
        {
            return;
        }

        for (int i = 0; i < ticks; i++)
        {
            Tick(settings);
        }
    }

    public string DebugStats()
    {
        return $"cellsConverted={cellsConverted} convertibleCount={convertibleCount} coverage={Coverage:P2} " +
               $"clusterRadius={ClusterRadius:F1} maxR={maxR:F1} frontier={frontierSet.Count} " +
               $"cellCredits={cellCredits:F1} cellBudget={cellBudget:F2} flipped={flipped} feeding={feeding}";
    }

    private void Tick(Settings settings)
    {
        ticksSinceReclassify++;
        if (ticksSinceReclassify >= ReclassifyIntervalTicks)
        {
            ticksSinceReclassify = 0;
            Reclassify();
        }

        ticksSinceFlipCheck++;
        if (!flipped && ticksSinceFlipCheck >= FlipCheckIntervalTicks)
        {
            ticksSinceFlipCheck = 0;
            if (convertibleCount > 0 && cellsConverted / (float)convertibleCount >= settings.biomeFlipThreshold)
            {
                TryFlipBiome();
            }
        }

        if (feeding && seedCell.IsValid && convertibleCount > 0)
        {
            AdvanceOneTick(settings);
        }
    }

    private void AdvanceOneTick(Settings settings)
    {
        float ticksPerCell = TicksPerCell(settings);
        if (ticksPerCell <= 0f)
        {
            return;
        }

        cellBudget += 1f / ticksPerCell;

        ticksSinceSteelDraw++;
        if (ticksSinceSteelDraw >= SteelDrawIntervalTicks)
        {
            ticksSinceSteelDraw = 0;
            DrawSteel(settings);
        }

        int placements = 0;
        while (placements < MaxPlacementsPerTick && cellBudget >= 1f && cellCredits >= settings.latticeSteelPerCell)
        {
            if (!TryPlaceOneCell(settings))
            {
                break; // walker mid-flight or frontier empty this attempt - resume next tick
            }

            placements++;
            cellBudget -= 1f;
            cellCredits -= settings.latticeSteelPerCell;
        }
    }

    private float TicksPerCell(Settings settings) => settings.latticeTargetDays * GenDate.TicksPerDay / convertibleCount;

    private void DrawSteel(Settings settings)
    {
        float ticksPerCell = TicksPerCell(settings);
        if (ticksPerCell <= 0f)
        {
            return;
        }

        float cellsThisWindow = SteelDrawIntervalTicks / ticksPerCell;
        float steelNeeded = cellsThisWindow * settings.latticeSteelPerCell;
        if (steelNeeded <= 0f)
        {
            return;
        }

        int stored = CompCerebrexCore.SumStored(map, ThingDefOf.Steel);
        float reserve = settings.latticeSteelReserve;

        if (feedingPaused)
        {
            float resumeThreshold = reserve + Mathf.Max(25f, reserve * 0.1f);
            if (stored < resumeThreshold)
            {
                return;
            }

            feedingPaused = false;
        }

        if (stored - steelNeeded < reserve)
        {
            feedingPaused = true;
            return;
        }

        int taken = CompCerebrexCore.ConsumeFromStorage(map, ThingDefOf.Steel, Mathf.CeilToInt(steelNeeded));
        cellCredits += taken;
    }

    private bool TryPlaceOneCell(Settings settings)
    {
        return Rand.Chance(DlaFraction(settings)) ? AdvanceDla(settings) : TryThicken();
    }

    private float DlaFraction(Settings settings)
    {
        if (convertibleCount <= 0)
        {
            return 0f;
        }

        float coverage = Mathf.Clamp01(cellsConverted / (float)convertibleCount);
        float baseBlend = Mathf.Lerp(0.35f, 0.04f, coverage);
        return Mathf.Clamp(baseBlend * (settings.latticeDlaFraction / 0.10f), 0f, 0.9f);
    }

    private bool AdvanceDla(Settings settings)
    {
        if (!walkerPos.IsValid)
        {
            if (Rand.Chance(settings.latticeRunnerChance))
            {
                return SpawnRunnerIsland();
            }

            SpawnWalker();
            if (!walkerPos.IsValid)
            {
                return false; // no valid birth point found (seed too close to map edge) - try again next call
            }
        }

        return StepWalker();
    }

    private void SpawnWalker()
    {
        float birthRadius = ClusterRadius + BirthRingOffset;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
            IntVec3 candidate = seedCell + new IntVec3(
                Mathf.RoundToInt(Mathf.Cos(angle) * birthRadius), 0, Mathf.RoundToInt(Mathf.Sin(angle) * birthRadius));

            if (candidate.InBounds(map))
            {
                walkerPos = candidate;
                walkerSteps = 0;
                return;
            }
        }

        walkerPos = IntVec3.Invalid;
    }

    private bool StepWalker()
    {
        float killRadius = ClusterRadius + KillRingOffset;
        float killRadiusSq = killRadius * killRadius;

        for (int i = 0; i < MaxWalkerStepsPerTick; i++)
        {
            if (walkerSteps > MaxWalkerStepsTotal || (walkerPos - seedCell).LengthHorizontalSquared > killRadiusSq)
            {
                AbandonWalker();
                return false;
            }

            if (IsAdjacentToCluster(walkerPos) && TryConvertCell(walkerPos))
            {
                AbandonWalker();
                return true;
            }

            walkerPos += GenAdj.CardinalDirections[Rand.Range(0, 4)];
            walkerSteps++;
        }

        return false; // still walking, resume next tick
    }

    private void AbandonWalker()
    {
        walkerPos = IntVec3.Invalid;
        walkerSteps = 0;
    }

    /// <summary>Runners are placed directly rather than walked - a true long-range walk from a huge
    /// birth radius is the one genuinely expensive shape DLA can take, and the visual result (an
    /// isolated island ahead of the front, absorbed as the radius cap grows to reach it) is identical.</summary>
    private bool SpawnRunnerIsland()
    {
        float dist = ClusterRadius * Rand.Range(1.3f, 2.0f);
        float angle = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
        IntVec3 candidate = seedCell + new IntVec3(
            Mathf.RoundToInt(Mathf.Cos(angle) * dist), 0, Mathf.RoundToInt(Mathf.Sin(angle) * dist));

        return candidate.InBounds(map) && TryConvertCell(candidate);
    }

    private bool TryThicken()
    {
        if (frontierList.Count == 0)
        {
            return false;
        }

        bool pureRandom = Rand.Chance(PureRandomFrontierChance);
        int samples = pureRandom ? 1 : ThickenSampleK;
        float radiusSq = ClusterRadius * ClusterRadius;

        int bestIdx = -1;
        int bestNeighbors = int.MaxValue;

        for (int i = 0; i < samples; i++)
        {
            int idx = FrontierSampleValid();
            if (idx < 0)
            {
                break;
            }

            IntVec3 c = map.cellIndices.IndexToCell(idx);

            // Thickening only fills behind the DLA front - letting it race ahead of R would blur the
            // fractal silhouette the front is meant to carry all the way through.
            if (!pureRandom && (c - seedCell).LengthHorizontalSquared > radiusSq)
            {
                continue;
            }

            if (pureRandom)
            {
                bestIdx = idx;
                break;
            }

            int neighbors = CountConvertedCardinalNeighbors(c);
            if (neighbors < bestNeighbors)
            {
                bestNeighbors = neighbors;
                bestIdx = idx;
            }
        }

        return bestIdx >= 0 && TryConvertCell(map.cellIndices.IndexToCell(bestIdx));
    }

    private bool TryConvertCell(IntVec3 c)
    {
        if (!c.InBounds(map))
        {
            return false;
        }

        int idx = map.cellIndices.CellToIndex(c);
        TerrainDef current = map.terrainGrid.topGrid[idx];
        if (current == CerebrexLatticeDefs.TargetTerrain || IsProtected(c, current))
        {
            return false;
        }

        map.terrainGrid.SetTerrain(c, CerebrexLatticeDefs.TargetTerrain);
        cellsConverted++;
        RegisterConverted(c, idx);
        return true;
    }

    private bool IsProtected(IntVec3 c, TerrainDef t)
    {
        return t.IsWater
            || t.Removable
            || t.BuildableByPlayer
            || t.passability == Traversability.Impassable
            || map.terrainGrid.FoundationAt(c) != null;
    }

    private void RegisterConverted(IntVec3 c, int idx)
    {
        frontierSet.Remove(idx);
        IncrementBlockDensity(c);

        foreach (IntVec3 offset in GenAdj.CardinalDirections)
        {
            IntVec3 n = c + offset;
            if (!n.InBounds(map))
            {
                continue;
            }

            int nIdx = map.cellIndices.CellToIndex(n);
            TerrainDef nt = map.terrainGrid.topGrid[nIdx];
            if (nt == CerebrexLatticeDefs.TargetTerrain || IsProtected(n, nt))
            {
                continue;
            }

            FrontierAdd(nIdx);
        }
    }

    private bool IsAdjacentToCluster(IntVec3 c)
    {
        foreach (IntVec3 offset in GenAdj.AdjacentCells)
        {
            IntVec3 n = c + offset;
            if (n.InBounds(map) && map.terrainGrid.topGrid[map.cellIndices.CellToIndex(n)] == CerebrexLatticeDefs.TargetTerrain)
            {
                return true;
            }
        }

        return false;
    }

    private int CountConvertedCardinalNeighbors(IntVec3 c)
    {
        int count = 0;
        foreach (IntVec3 offset in GenAdj.CardinalDirections)
        {
            IntVec3 n = c + offset;
            if (n.InBounds(map) && map.terrainGrid.topGrid[map.cellIndices.CellToIndex(n)] == CerebrexLatticeDefs.TargetTerrain)
            {
                count++;
            }
        }

        return count;
    }

    private void FrontierAdd(int idx)
    {
        if (frontierSet.Add(idx))
        {
            frontierList.Add(idx);
        }
    }

    /// <summary>Pops a random still-valid frontier cell, lazily swap-removing stale entries
    /// (already converted, or dropped by <see cref="Reclassify"/>) along the way.</summary>
    private int FrontierSampleValid()
    {
        while (frontierList.Count > 0)
        {
            int i = Rand.Range(0, frontierList.Count);
            int idx = frontierList[i];
            if (frontierSet.Contains(idx))
            {
                return idx;
            }

            int last = frontierList.Count - 1;
            frontierList[i] = frontierList[last];
            frontierList.RemoveAt(last);
        }

        return -1;
    }

    private float ClusterRadius
    {
        get
        {
            if (convertibleCount <= 0 || maxR <= 0f)
            {
                return MinClusterRadius;
            }

            float progress = Mathf.Clamp01(cellsConverted / (float)convertibleCount);
            return Mathf.Clamp(maxR * Mathf.Sqrt(progress) * FrontLeadFactor, MinClusterRadius, maxR);
        }
    }

    private void RecomputeMaxR()
    {
        if (!seedCell.IsValid)
        {
            maxR = 0f;
            return;
        }

        IntVec3[] corners =
        {
            new(0, 0, 0),
            new(map.Size.x - 1, 0, 0),
            new(0, 0, map.Size.z - 1),
            new(map.Size.x - 1, 0, map.Size.z - 1),
        };

        float best = 0f;
        foreach (IntVec3 corner in corners)
        {
            float d = Mathf.Sqrt((corner - seedCell).LengthHorizontalSquared);
            if (d > best)
            {
                best = d;
            }
        }

        maxR = best;
    }

    private void InitBlockGrid()
    {
        blocksX = Mathf.CeilToInt(map.Size.x / (float)BlockSize);
        blocksZ = Mathf.CeilToInt(map.Size.z / (float)BlockSize);
        blockConvertedCounts = new int[blocksX * blocksZ];
    }

    private int BlockIndex(int bx, int bz) => bz * blocksX + bx;

    private void IncrementBlockDensity(IntVec3 c)
    {
        blockConvertedCounts[BlockIndex(c.x / BlockSize, c.z / BlockSize)]++;
    }

    /// <summary>
    /// Full ground-truth pass over the terrain grid. Recomputes convertibleCount, cellsConverted,
    /// the frontier and the block density grid together in one scan - this is what lets a player
    /// re-floor over previously-converted ground (reclaiming territory) or remove a floor (exposing
    /// new convertible ground) get picked up correctly, without patching floor placement itself.
    /// </summary>
    private void Reclassify()
    {
        if (blockConvertedCounts == null)
        {
            InitBlockGrid();
        }

        convertibleCount = 0;
        cellsConverted = 0;
        frontierList.Clear();
        frontierSet.Clear();
        Array.Clear(blockConvertedCounts, 0, blockConvertedCounts.Length);

        TerrainDef[] top = map.terrainGrid.topGrid;
        for (int idx = 0; idx < top.Length; idx++)
        {
            TerrainDef t = top[idx];
            IntVec3 c = map.cellIndices.IndexToCell(idx);
            bool isTarget = t == CerebrexLatticeDefs.TargetTerrain;
            bool blocked = IsProtected(c, t);

            if (!blocked)
            {
                convertibleCount++;
            }

            if (isTarget)
            {
                cellsConverted++;
                IncrementBlockDensity(c);
            }
            else if (!blocked && CountConvertedCardinalNeighbors(c) > 0)
            {
                FrontierAdd(idx);
            }
        }
    }

    private void TryFlipBiome()
    {
        if (flipped || map.IsPocketMap || map.IsTempIncidentMap || !map.Tile.Valid)
        {
            return;
        }

        BiomeDef target = CerebrexLatticeDefs.TargetBiome;
        BiomeDef old = map.Biome;

        if (old == target)
        {
            return;
        }

        if (old.inVacuum != target.inVacuum)
        {
            ModLog.Warn($"Cerebrex lattice: refusing biome flip on {map} - inVacuum mismatch (old={old.inVacuum}, target={target.inVacuum}).");
            return;
        }

        ApplyBiomeFlip(alreadyFlipped: false);
    }

    /// <summary>
    /// Mutates the world tile's biome and invalidates the caches that read it. The invalidation
    /// step is deliberately fault-tolerant: several of those caches are only reachable via
    /// reflection into RimWorld privates, verified once against decompiled source rather than
    /// compiled and run. If a field name is wrong or renamed by a future patch, a failure there
    /// must not abort the whole routine - otherwise the world tile's biome would already be
    /// mutated with no letter ever sent and no "flipped" flag set, so the 2000-tick retry gate
    /// would attempt (and fail) the exact same call forever.
    /// </summary>
    private void ApplyBiomeFlip(bool alreadyFlipped)
    {
        BiomeDef target = CerebrexLatticeDefs.TargetBiome;
        BiomeDef old = map.Biome;

        if (old != target)
        {
            map.Tile.Tile.PrimaryBiome = target;
        }

        try
        {
            InvalidateBiomeCaches(old, target);
        }
        catch (Exception e)
        {
            ModLog.Error("Cerebrex lattice: unexpected failure invalidating biome-derived caches after flip - " +
                         "some vanilla systems (wild plants, pathing, world redraw) may show stale data until next save/load.", e);
        }

        if (alreadyFlipped)
        {
            return;
        }

        flipped = true;
        flippedTick = Find.TickManager.TicksGame;
        flippedOnTileId = map.Tile.tileId;

        Find.LetterStack.ReceiveLetter(
            "CerebrexFlavourPack_BiomeFlipped_LetterLabel".Translate(),
            "CerebrexFlavourPack_BiomeFlipped_LetterText".Translate(map.Parent.LabelCap),
            LetterDefOf.NegativeEvent,
            new LookTargets(map.Center, map));

        ModLog.Log($"Cerebrex lattice: {map.Parent.LabelCap} biome flipped to {target.defName} at {cellsConverted}/{convertibleCount} coverage.");
    }

    private void InvalidateBiomeCaches(BiomeDef old, BiomeDef target)
    {
        InvalidateWildPlantSpawnerCaches();

        try
        {
            map.plantGrowthRateCalculator.BuildFor(map);
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: plantGrowthRateCalculator.BuildFor failed: {e.Message}");
        }

        try
        {
            MixedBiomeMapComponent mixedBiomeComp = map.MixedBiomeComp;
            if (mixedBiomeComp != null)
            {
                mixedBiomeComp.biomeGrid = null;
                mixedBiomeComp.secondaryBiome = null;
            }
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: MixedBiomeMapComponent cache clear failed: {e.Message}");
        }

        try
        {
            Tile tile = map.Tile.Tile;
            // Set the "have I checked" flag back to null (not false) - false would permanently
            // report "no secondary biome" without ever re-checking; null is what actually triggers
            // Tile.Biomes to recompute on next access.
            AccessTools.Field(typeof(Tile), "tmpHasSecondaryBiome")?.SetValue(tile, null);
            AccessTools.Field(typeof(Tile), "tmpSecondaryBiome")?.SetValue(tile, null);
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: Tile secondary-biome cache clear failed: {e.Message}");
        }

        try
        {
            Find.WorldPathGrid.RecalculatePerceivedMovementDifficultyAt(map.Tile, out bool needsRecache);
            if (needsRecache)
            {
                Find.WorldReachability.ClearCache();
            }
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: world path-grid recalculation failed: {e.Message}");
        }

        try
        {
            if (old.disableShadows != target.disableShadows)
            {
                AccessTools.Field(typeof(MapDrawer), "sections")?.SetValue(map.mapDrawer, null);
                map.mapDrawer.RegenerateEverythingNow();
            }
            else
            {
                map.mapDrawer.WholeMapChanged(MapMeshFlagDefOf.Terrain | MapMeshFlagDefOf.Things);
            }
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: map mesh regeneration failed: {e.Message}");
        }

        try
        {
            Find.World.renderer.SetDirty<WorldDrawLayer_Terrain>(map.Tile.Layer);
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: world-map redraw dirty failed: {e.Message}");
        }
    }

    /// <summary>
    /// Every field here is private on WildPlantSpawner and was verified once by reading decompiled
    /// source, not by compiling against it. Each is nulled independently so a single wrong/renamed
    /// field name degrades to a logged warning instead of aborting the biome flip - see the warning
    /// on <see cref="ApplyBiomeFlip"/>.
    /// </summary>
    private void InvalidateWildPlantSpawnerCaches()
    {
        object spawner = map.wildPlantSpawner;
        if (spawner == null)
        {
            return;
        }

        Type spawnerType = typeof(WildPlantSpawner);
        string[] cacheFields =
        {
            "cachedPlantCommonalities",
            "cachedWildPlants",
            "cachedMutatorWildPlants",
            "cachedPlantsCommonalitiesSum",
            "cachedCavePlantsCommonalitiesSum",
            "cachedHaveAnyPlantsWhichIgnoreFertility",
            "cachedOverrideDensityForFertilityCurve",
        };

        foreach (string fieldName in cacheFields)
        {
            try
            {
                FieldInfo field = AccessTools.Field(spawnerType, fieldName);
                if (field == null)
                {
                    ModLog.Warn($"Cerebrex lattice: WildPlantSpawner field '{fieldName}' not found (RimWorld version drift?) - skipping.");
                    continue;
                }

                field.SetValue(spawner, null);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Cerebrex lattice: failed to clear WildPlantSpawner.{fieldName}: {e.Message}");
            }
        }

        try
        {
            AccessTools.Field(spawnerType, "hasWholeMapNumDesiredPlantsCalculated")?.SetValue(spawner, false);
        }
        catch (Exception e)
        {
            ModLog.Warn($"Cerebrex lattice: failed to reset hasWholeMapNumDesiredPlantsCalculated: {e.Message}");
        }
    }
}
