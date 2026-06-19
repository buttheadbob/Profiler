using System.Collections.Generic;

namespace Profiler
{
    public class DiagnoseResult
    {
        public ulong TotalFrameCount { get; set; }
        public double TotalTimeMs { get; set; }
        public Dictionary<string, double> FrameCategories { get; set; }
        public List<GridEntry> TopGrids { get; set; }
        public List<BlockTypeEntry> TopBlockTypes { get; set; }
        public List<PlayerEntry> TopPlayers { get; set; }
        public List<FactionEntry> TopFactions { get; set; }
        public List<ClusterEntry> TopClusters { get; set; }
        public List<SessionEntry> TopSessionComponents { get; set; }
    }

    public class GridEntry
    {
        public string Name { get; set; }
        public long EntityId { get; set; }
        public double MsPerFrame { get; set; }
        public int BlockCount { get; set; }
    }

    public class BlockTypeEntry
    {
        public string TypeName { get; set; }
        public double MsPerFrame { get; set; }
    }

    public class PlayerEntry
    {
        public string Name { get; set; }
        public long IdentityId { get; set; }
        public double MsPerFrame { get; set; }
        public int GridCount { get; set; }
    }

    public class FactionEntry
    {
        public string Tag { get; set; }
        public long FactionId { get; set; }
        public double MsPerFrame { get; set; }
        public int GridCount { get; set; }
    }

    public class ClusterEntry
    {
        public int Index { get; set; }
        public int GridCount { get; set; }
        public double SizeKm { get; set; }
        public double MsPerFrame { get; set; }
        public List<GridEntry> TopGrids { get; set; }
    }

    public class SessionEntry
    {
        public string ComponentName { get; set; }
        public double MsPerFrame { get; set; }
    }
}
