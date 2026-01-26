namespace Companella.Mods;

public class ExampleMod : BaseMod
{
	public override string Name => "Nothing";
	public override string Description => "Does absolutely nothing";
	public override string Category => "General";
	public override string Icon => "N";

	protected override ModResult ApplyInternal(ModContext context)
	{
		return ModResult.Succeeded(context.HitObjects, null);
	}
}
