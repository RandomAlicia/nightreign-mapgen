using System.Collections.Generic;

namespace NightReign.MapGen
{
    public sealed class SummaryRoot
    {
        public SummaryPattern[]? Patterns { get; set; }
    }

    public sealed class SummaryPattern
    {
        public string? Id             { get; set; }
        public string? Nightlord      { get; set; }
        public string? Special        { get; set; }
        public string? Treasure       { get; set; }
        public string? Frenzy_Tower   { get; set; }   // "North" / "South"
        public string? Rot_Blessing   { get; set; }   // "west"/"northwest"/"southwest"
        public string? Spawn_Point_Id { get; set; }   // maps from "spawn_point_id"
    }

    public sealed class PatternDoc
    {
        public int? patternId { get; set; }
        public string? Nightlord { get; set; }
        public string? SpecialEvent { get; set; }
        public List<Poi>? pois { get; set; }
    }

    public sealed class Poi
    {
        public double x { get; set; }
        public double z { get; set; }
        public string? name { get; set; }
        public int? dupCount { get; set; }
        public List<string>? names { get; set; }
    }

    public sealed class IndexEntry
    {
        public string? name { get; set; }
        public string? category { get; set; }
        public string? icon { get; set; }
        public string? cid { get; set; }
        public string? id { get; set; }
    }
}
