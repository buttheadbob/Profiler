using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Profiler.Basics;
using Profiler.Core;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Utils.General;
using Utils.Torch;
using TaskUtils = Utils.General.TaskUtils;

namespace Profiler
{
    public static class ProfilerApi
    {
        /// <summary>
        /// Runs a comprehensive performance diagnostic. Called by external plugins.
        /// </summary>
        /// <param name="seconds">Profiling duration (default 10)</param>
        /// <param name="topCount">Max entities returned per category, including clusters (default 5)</param>
        /// <returns>Structured results with all timing in ms/frame</returns>
        public static async Task<DiagnoseResult> RunDiagnoseAsync(int seconds = 10, int topCount = 5)
        {
            var gameLoop = new GameLoopProfiler();
            var grids = new GridProfiler(GameEntityMask.Empty);
            var blockTypes = new BlockTypeProfiler(GameEntityMask.Empty);
            var players = new PlayerProfiler(GameEntityMask.Empty);
            var factions = new FactionProfiler(GameEntityMask.Empty);
            var physics = new PhysicsProfiler();
            var session = new SessionComponentsProfiler();

            using (ProfilerResultQueue.Profile(gameLoop))
            using (ProfilerResultQueue.Profile(grids))
            using (ProfilerResultQueue.Profile(blockTypes))
            using (ProfilerResultQueue.Profile(players))
            using (ProfilerResultQueue.Profile(factions))
            using (ProfilerResultQueue.Profile(physics))
            using (ProfilerResultQueue.Profile(session))
            {
                gameLoop.MarkStart();
                grids.MarkStart();
                blockTypes.MarkStart();
                players.MarkStart();
                factions.MarkStart();
                physics.MarkStart();
                session.MarkStart();

                await Task.Delay(TimeSpan.FromSeconds(seconds));
                await Task.Delay(150);

                gameLoop.MarkEnd();
                grids.MarkEnd();
                blockTypes.MarkEnd();
                players.MarkEnd();
                factions.MarkEnd();
                physics.MarkEnd();
                session.MarkEnd();
            }

            var glResult = gameLoop.GetResult();
            var gridResult = grids.GetResult();
            var btResult = blockTypes.GetResult();
            var playerResult = players.GetResult();
            var factionResult = factions.GetResult();
            var physResult = physics.GetResult();
            var sessionResult = session.GetResult();

            var topClusters = physResult.GetTopEntities(topCount).ToArray();
            var clusterEntries = new List<ClusterEntry>();
            var playerGridCounts = new Dictionary<long, int>();
            var factionGridCounts = new Dictionary<long, int>();

            try
            {
                await VRageUtils.MoveToGameLoop();

                foreach (var (world, entry, ci) in topClusters.Select((kv, i) => (kv.Key, kv.Entity, i)))
                {
                    var entities = world.GetEntities().OfType<MyCubeGrid>().ToArray();
                    var gridCount = entities.Length;
                    var (size, _) = VRageUtils.GetBound(entities.Select(e => e.PositionComp.GetPosition()));
                    var sizeKm = size / 1000.0;
                    var msPerFrame = entry.MainThreadTime / physResult.TotalFrameCount;

                    var clusterGridInfos = new List<GridEntry>();
                    foreach (var g in entities)
                    {
                        if (gridResult.TryGet(g, out var ge))
                        {
                            clusterGridInfos.Add(new GridEntry
                            {
                                Name = g.DisplayName,
                                EntityId = g.EntityId,
                                MsPerFrame = ge.MainThreadTime / gridResult.TotalFrameCount,
                                BlockCount = g.BlocksCount,
                            });
                        }
                    }

                    clusterEntries.Add(new ClusterEntry
                    {
                        Index = ci,
                        GridCount = gridCount,
                        SizeKm = sizeKm,
                        MsPerFrame = msPerFrame,
                        TopGrids = clusterGridInfos
                            .OrderByDescending(x => x.MsPerFrame)
                            .Take(3)
                            .ToList(),
                    });
                }

                foreach (var group in MyCubeGridGroups.Static.Logical.Groups)
                foreach (var node in group.Nodes)
                {
                    var grid = node.NodeData;
                    foreach (var ownerId in grid.BigOwners)
                    {
                        playerGridCounts.TryGetValue(ownerId, out var pc);
                        playerGridCounts[ownerId] = pc + 1;

                        var f = MySession.Static.Factions.TryGetPlayerFaction(ownerId);
                        if (f != null)
                        {
                            factionGridCounts.TryGetValue(f.FactionId, out var fc);
                            factionGridCounts[f.FactionId] = fc + 1;
                        }
                    }
                }
            }
            finally
            {
                await TaskUtils.MoveToThreadPool();
            }

            var physTotalMs = physResult.GetTopEntities().Sum(kv => kv.Entity.MainThreadTime) / physResult.TotalFrameCount;

            var result = new DiagnoseResult
            {
                TotalFrameCount = glResult.TotalFrameCount,
                TotalTimeMs = glResult.TotalTime,
                FrameCategories = new Dictionary<string, double>
                {
                    ["Update"] = GetCategoryMs(glResult, ProfilerCategory.Update),
                    ["Physics"] = physTotalMs,
                    ["Network"] = GetCategoryMs(glResult, ProfilerCategory.UpdateNetwork),
                    ["Replication"] = GetCategoryMs(glResult, ProfilerCategory.UpdateReplication),
                    ["Session"] = GetCategoryMs(glResult, ProfilerCategory.UpdateSessionComponents),
                    ["GPS"] = GetCategoryMs(glResult, ProfilerCategory.UpdateGps),
                    ["ParallelWait"] = GetCategoryMs(glResult, ProfilerCategory.UpdateParallelWait),
                    ["Lock"] = GetCategoryMs(glResult, ProfilerCategory.Lock),
                },
                TopGrids = gridResult.GetTopEntities(topCount)
                    .Select(kv => new GridEntry
                    {
                        Name = kv.Key.DisplayName,
                        EntityId = kv.Key.EntityId,
                        MsPerFrame = kv.Entity.MainThreadTime / gridResult.TotalFrameCount,
                        BlockCount = kv.Key.BlocksCount,
                    }).ToList(),
                TopBlockTypes = btResult.GetTopEntities(topCount)
                    .Select(kv => new BlockTypeEntry
                    {
                        TypeName = BlockTypeToString(kv.Key),
                        MsPerFrame = kv.Entity.MainThreadTime / btResult.TotalFrameCount,
                    }).ToList(),
                TopPlayers = playerResult.GetTopEntities(topCount)
                    .Select(kv => new PlayerEntry
                    {
                        Name = kv.Key.DisplayName,
                        IdentityId = kv.Key.IdentityId,
                        MsPerFrame = kv.Entity.MainThreadTime / playerResult.TotalFrameCount,
                        GridCount = playerGridCounts.TryGetValue(kv.Key.IdentityId, out var pc) ? pc : 0,
                    }).ToList(),
                TopFactions = factionResult.GetTopEntities(topCount)
                    .Select(kv => new FactionEntry
                    {
                        Tag = kv.Key.Tag,
                        FactionId = kv.Key.FactionId,
                        MsPerFrame = kv.Entity.MainThreadTime / factionResult.TotalFrameCount,
                        GridCount = factionGridCounts.TryGetValue(kv.Key.FactionId, out var fc) ? fc : 0,
                    }).ToList(),
                TopClusters = clusterEntries,
                TopSessionComponents = sessionResult.GetTopEntities(topCount)
                    .Select(kv => new SessionEntry
                    {
                        ComponentName = kv.Key.GetType().Name,
                        MsPerFrame = kv.Entity.MainThreadTime / sessionResult.TotalFrameCount,
                    }).ToList(),
            };

            gameLoop.Dispose();
            grids.Dispose();
            blockTypes.Dispose();
            players.Dispose();
            factions.Dispose();
            physics.Dispose();
            session.Dispose();

            return result;
        }

        static double GetCategoryMs(BaseProfilerResult<ProfilerCategory> result, ProfilerCategory category)
        {
            return result.TryGet(category, out var e) ? e.MainThreadTime / result.TotalFrameCount : 0;
        }

        static string BlockTypeToString(Type type)
        {
            return type.ToString().Split('.').LastOrDefault() ?? "unknown";
        }
    }
}
