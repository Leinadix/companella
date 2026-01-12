using OsuMappingHelper.Models;

namespace OsuMappingHelper.Models;

public class NoLNMod : BaseMod
{
    public override string Name => "No LN";
    public override string Description => "Removes all long notes from the beatmap";
    public override string Category => "General";
    public override string Icon => "NLN";
    
    protected override ModResult ApplyInternal(ModContext context)
    {
        
        var modified = context.HitObjects.Select(ho =>
        {
            if (ho.IsHold)
            {
                var clone = ho.Clone();
                clone.Type = HitObjectType.Circle;
                clone.EndTime = clone.Time;
                return clone;
            }
            return ho;
        }).ToList();
        
        var stats = CalculateStatistics(context.HitObjects, modified);
        return ModResult.Succeeded(modified, stats);
    }
}