using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("func_breakable")]
public class Breakable : BaseEntity, IBreakableWithPropData
{
	public void SetDmgModBullet(float dmgMod) => DmgModBullet = dmgMod;
	public void SetDmgModClub(float dmgMod) => DmgModClub = dmgMod;
	public void SetDmgModExplosive(float dmgMod) => DmgModExplosive = dmgMod;
	public float GetDmgModBullet() => DmgModBullet;
	public float GetDmgModClub() => DmgModClub;
	public float GetDmgModExplosive() => DmgModExplosive;
	public void SetExplosiveRadius(float radius) => ExplodeRadius = radius;
	public void SetExplosiveDamage(float damage) => ExplosionMagnitude = (int)damage;
	public float GetExplosiveRadius() => ExplodeRadius;
	public float GetExplosiveDamage() => ExplosionMagnitude;
	public void SetPhysicsDamageTable(ReadOnlySpan<char> tableName) => PhysicsDamageTableName = tableName.IsEmpty ? null : new(tableName);
	public ReadOnlySpan<char> GetPhysicsDamageTable() => PhysicsDamageTableName;
	public void SetBreakableModel(ReadOnlySpan<char> model) => BreakableModel = model.IsEmpty ? null : new(model);
	public ReadOnlySpan<char> GetBreakableModel() => BreakableModel;
	public void SetBreakableSkin(int skin) => BreakableSkin = skin;
	public int GetBreakableSkin() => BreakableSkin;
	public void SetBreakableCount(int count) => BreakableCount = count;
	public int GetBreakableCount() => BreakableCount;
	public void SetMaxBreakableSize(int size) => MaxBreakableSize = size;
	public int GetMaxBreakableSize() => MaxBreakableSize;
	public void SetPropDataBlocksLOS(bool blocksLOS) => SetBlocksLOS(blocksLOS);
	public void SetPropDataIsAIWalkable(bool blocksLOS) => SetAIWalkable(blocksLOS);
	public void SetBasePropData(ReadOnlySpan<char> propdata) => BasePropData = propdata.IsEmpty ? null : new(propdata);
	public ReadOnlySpan<char> GetBasePropData() => BasePropData;
	public void SetInteraction(PropDataInteractions interaction) => Interactions |= (1 << (int)interaction);
	public bool HasInteraction(PropDataInteractions interaction) => (Interactions & (1 << (int)interaction)) != 0;
	public void SetPhysicsMode(PhysicsMultiplayerMode mode) { }
	public PhysicsMultiplayerMode GetPhysicsMode() => PhysicsMultiplayerMode.Solid;
	public void SetMultiplayerBreakMode(MultiplayerBreak mode) { }
	public MultiplayerBreak GetMultiplayerBreakMode() => MultiplayerBreak.Default;

	protected float DmgModBullet;
	protected float DmgModClub;
	protected float DmgModExplosive;
	protected string? PhysicsDamageTableName;
	protected string? BreakableModel;
	protected int BreakableSkin;
	protected int BreakableCount;
	protected int MaxBreakableSize;
	protected string? BasePropData;
	protected int Interactions;
	protected int ExplosionMagnitude;
	protected float ExplodeRadius;
}
