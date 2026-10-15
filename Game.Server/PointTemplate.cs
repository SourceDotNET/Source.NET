using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
using System.Text;

namespace Game.Server;

using DEFINE = Source.DEFINE<Game.Server.PointTemplate>;

public class PointTemplatePrecacher() : AutoGameSystem("CPointTemplatePrecacher")
{
	public static readonly PointTemplatePrecacher g_PointTemplatePrecacher = new();

	readonly List<Handle<PointTemplate>> Ents = [];

	public void AddToPrecache(PointTemplate ent) {
		Handle<PointTemplate> handle = new();
		handle.Set(ent);
		Ents.Add(handle);
	}

	public override void LevelInitPreEntity() => Ents.Clear();

	public override void Shutdown() => Ents.Clear();

	public void Precache() {
		int c = Ents.Count;
		for (int i = 0; i < c; ++i) {
			PointTemplate? ent = Ents[i].Get();
			ent?.PerformPrecache();
		}

		Ents.Clear();
	}

	public static void PrecachePointTemplates() => g_PointTemplatePrecacher.Precache();
}

[LinkEntityToClass("point_template")]
public class PointTemplate : LogicalEntity
{
	public const int MAX_NUM_TEMPLATES = 16;

	const int SF_POINTTEMPLATE_DONTREMOVETEMPLATEENTITIES = 0x0001;
	const int SF_POINTTEMPLATE_PRESERVE_NAMES = 0x0002;

	public struct Template
	{
		public int TemplateIndex;
		public Matrix4x4 EntityToTemplate;
	}

	string? TemplateEntityName01, TemplateEntityName02, TemplateEntityName03, TemplateEntityName04, TemplateEntityName05, TemplateEntityName06, TemplateEntityName07, TemplateEntityName08,
		TemplateEntityName09, TemplateEntityName10, TemplateEntityName11, TemplateEntityName12, TemplateEntityName13, TemplateEntityName14, TemplateEntityName15, TemplateEntityName16;

	readonly List<EHANDLE> TemplateEntities = [];
	readonly List<Template> Templates = [];

	readonly OutputEvent OutputOnSpawned = new();

	string? TemplateEntityName(int i) => i switch {
		0 => TemplateEntityName01, 1 => TemplateEntityName02, 2 => TemplateEntityName03, 3 => TemplateEntityName04,
		4 => TemplateEntityName05, 5 => TemplateEntityName06, 6 => TemplateEntityName07, 7 => TemplateEntityName08,
		8 => TemplateEntityName09, 9 => TemplateEntityName10, 10 => TemplateEntityName11, 11 => TemplateEntityName12,
		12 => TemplateEntityName13, 13 => TemplateEntityName14, 14 => TemplateEntityName15, 15 => TemplateEntityName16,
		_ => null
	};

	public static readonly new DataMap DataDesc = new(typeof(PointTemplate), LogicalEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(TemplateEntityName01), FieldType.String, "Template01"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName02), FieldType.String, "Template02"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName03), FieldType.String, "Template03"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName04), FieldType.String, "Template04"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName05), FieldType.String, "Template05"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName06), FieldType.String, "Template06"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName07), FieldType.String, "Template07"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName08), FieldType.String, "Template08"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName09), FieldType.String, "Template09"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName10), FieldType.String, "Template10"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName11), FieldType.String, "Template11"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName12), FieldType.String, "Template12"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName13), FieldType.String, "Template13"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName14), FieldType.String, "Template14"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName15), FieldType.String, "Template15"),
		DEFINE.KEYFIELD(nameof(TemplateEntityName16), FieldType.String, "Template16"),

		DEFINE.INPUTFUNC(FieldType.Void, "ForceSpawn", nameof(InputForceSpawn), (INPUTFUNCPTR)((self, data) => ((PointTemplate)self).InputForceSpawn(data))),

		DEFINE.OUTPUT(nameof(OutputOnSpawned), "OnEntitySpawned", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() => Precache();

	public override void Precache() => PointTemplatePrecacher.g_PointTemplatePrecacher.AddToPrecache(this);

	public bool AllowNameFixup() => !HasSpawnFlags(SF_POINTTEMPLATE_PRESERVE_NAMES);

	public void StartBuildingTemplates() {
		for (int i = 0; i < MAX_NUM_TEMPLATES; i++) {
			string? templateEntityName = TemplateEntityName(i);
			if (templateEntityName != null) {
				BaseEntity? entity = null;
				int oldNum = TemplateEntities.Count;
				while ((entity = gEntList.FindEntityByName(entity, templateEntityName)) != null) {
					EHANDLE handle = new();
					handle.Set(entity);
					TemplateEntities.Add(handle);
				}

				if (oldNum == TemplateEntities.Count)
					Warning($"Couldn't find any entities named {templateEntityName}, which point_template {GetEntityName()} is specifying.\n");
			}
		}
	}

	public void FinishBuildingTemplates() {
		TemplateEntities.Clear();
		Game.Server.Templates.ReconnectIOForGroup(this);
	}

	public void AddTemplate(BaseEntity? entity, ReadOnlyMemory<byte> mapData, int len) {
		if (entity == null) {
			Warning($"point_template {GetEntityName()} failed to add template entity beacuse it is NULL. Did you add same entity multiple times?\n");
			return;
		}

		nint index = Game.Server.Templates.Add(entity, mapData.Span, len);
		if (index == -1) {
			Warning($"point_template {GetEntityName()} failed to add template.\n");
			return;
		}

		Template newTemplate = new() {
			TemplateIndex = (int)index
		};

		Matrix4x4 templateToWorld = default, entityToWorld = default;
		templateToWorld.SetupMatrixOrgAngles(GetAbsOrigin(), GetAbsAngles());
		MathLib.MatrixInverseTR(in templateToWorld, out Matrix4x4 worldToTemplate);
		entityToWorld.SetupMatrixOrgAngles(entity.GetAbsOrigin(), entity.GetAbsAngles());
		MathLib.MatrixMultiply(in worldToTemplate, in entityToWorld, out newTemplate.EntityToTemplate);

		Templates.Add(newTemplate);
	}

	public bool ShouldRemoveTemplateEntities() => (SpawnFlags & SF_POINTTEMPLATE_DONTREMOVETEMPLATEENTITIES) == 0;

	public int GetNumTemplates() => Templates.Count;

	public int GetTemplateIndexForTemplate(int template) {
		Assert(template < Templates.Count);
		return Templates[template].TemplateIndex;
	}

	public int GetNumTemplateEntities() => TemplateEntities.Count;

	public BaseEntity? GetTemplateEntity(int templateNumber) {
		Assert(templateNumber < TemplateEntities.Count);
		return TemplateEntities[templateNumber].Get();
	}

	public void PerformPrecache() {
		int templates = Templates.Count;
		if (templates == 0) {
			Msg($"Precache called on a point_template that has no templates: {GetEntityName()}\n");
			return;
		}

		Game.Server.Templates.StartUniqueInstance();

		for (int i = 0; i < templates; i++) {
			int templateIndex = Templates[i].TemplateIndex;

			ReadOnlyMemory<byte> mapData;
			if (AllowNameFixup() && Game.Server.Templates.IndexRequiresEntityIOFixup(templateIndex))
				mapData = Game.Server.Templates.GetEntityIOFixedMapData(templateIndex);
			else
				mapData = Game.Server.Templates.FindByIndex(templateIndex);

			int stringSize = Game.Server.Templates.GetStringSize(templateIndex);

			MapEntity_PrecacheEntity(mapData, stringSize);
		}
	}

	public bool CreateInstance(in Vector3 origin, in QAngle angles, List<BaseEntity> entities) {
		int templates = Templates.Count;
		if (templates == 0) {
			Msg($"CreateInstance called on a point_template that has no templates: {GetEntityName()}\n");
			return false;
		}

		Game.Server.Templates.StartUniqueInstance();

		HierarchicalSpawn_t[] spawnList = new HierarchicalSpawn_t[templates];
		MapLoadEntityFilter filter = new();

		int i;
		for (i = 0; i < templates; i++) {
			int templateIndex = Templates[i].TemplateIndex;

			ReadOnlyMemory<byte> mapData;
			if (AllowNameFixup() && Game.Server.Templates.IndexRequiresEntityIOFixup(templateIndex))
				mapData = Game.Server.Templates.GetEntityIOFixedMapData(templateIndex);
			else
				mapData = Game.Server.Templates.FindByIndex(templateIndex);

			ParseEntity(out BaseEntity? entity, mapData, filter);
			if (entity == null) {
				ReadOnlySpan<byte> text = mapData.Span;
				Msg($"Failed to initialize templated entity with mapdata: {Encoding.ASCII.GetString(text[..MapEntity.StrLen(text)])}\n");
				return false;
			}

			Matrix4x4 newTemplateToWorld = default;
			newTemplateToWorld.SetupMatrixOrgAngles(origin, angles);
			MathLib.MatrixMultiply(in newTemplateToWorld, Templates[i].EntityToTemplate, out Matrix4x4 storedLocalToWorld);

			Vector3 newOrigin = default;
			storedLocalToWorld.GetTranslation(ref newOrigin);
			MathLib.MatrixToAngles(in storedLocalToWorld, out QAngle newAngles);

			entity.SetAbsOrigin(newOrigin);
			entity.SetAbsAngles(newAngles);

			spawnList[i].Entity = entity;
			spawnList[i].Depth = 0;
			spawnList[i].DeferredParent = null!;
		}

		SpawnHierarchicalList(templates, spawnList, true);

		for (i = 0; i < templates; ++i) {
			if (spawnList[i].Entity != null)
				entities.Add(spawnList[i].Entity!);
		}

		return true;
	}

	void InputForceSpawn(InputData inputdata) {
		List<BaseEntity> newEntities = [];
		if (!CreateInstance(GetAbsOrigin(), GetAbsAngles(), newEntities))
			return;

		OutputOnSpawned.FireOutput(this, this);
	}
}
