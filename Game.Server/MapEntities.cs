global using static Game.Server.MapEntities;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Game.Server;

struct HierarchicalSpawn_t
{
	public BaseEntity? Entity;
	public int Depth;
	public BaseEntity DeferredParent;     // attachment parents can't be set until the parents are spawned
	public string DeferredParentAttachment; // so defer setting them up until the second pass
};

struct HierarchicalSpawnMapData_t
{
	public ReadOnlyMemory<byte> MapData;
	public int MapDataLength;
};

public interface IMapEntityFilter
{
	bool ShouldCreateEntity(ReadOnlySpan<char> className);
	BaseEntity? CreateNextEntity(ReadOnlySpan<char> className);
}

[InlineArray(EntityMapData.MAPKEY_MAXLENGTH)] public struct InlineArrayMapKeyMaxLength<T> { T first; }
public static class MapEntities
{
	static ref Edict? g_pForceAttachEdict => ref BaseEntity.g_pForceAttachEdict;

	// creates an entity by string name, but does not spawn it
	public static BaseEntity? CreateEntityByName(ReadOnlySpan<char> className, int forceEdictIndex = -1) {
		if (forceEdictIndex != -1) {
			g_pForceAttachEdict = engine.CreateEdict(forceEdictIndex);
			if (g_pForceAttachEdict == null)
				Error($"CreateEntityByName( {className}, {forceEdictIndex} ) - CreateEdict failed.");
		}

		IServerNetworkable? network = EntityFactoryDictionary().Create(className);
		g_pForceAttachEdict = null;

		if (network == null)
			return null;

		BaseEntity? entity = (BaseEntity?)network.GetBaseEntity();
		Assert(entity);
		return entity;
	}

	// Advance a Memory cursor by parsing one token out of it. Returns the remaining data
	// (Empty at end-of-data, mirroring MapEntity_ParseToken returning NULL).
	static ReadOnlyMemory<byte> ParseTokenAdvance(ReadOnlyMemory<byte> data, Span<byte> token) {
		ReadOnlySpan<byte> rest = MapEntity.ParseToken(data.Span, token);
		if (rest == null)
			return ReadOnlyMemory<byte>.Empty;
		return data[(data.Length - rest.Length)..];
	}

	// Skip to the beginning of the next entity in the data block (Memory cursor variant).
	static ReadOnlyMemory<byte> SkipToNextEntity(ReadOnlyMemory<byte> data, Span<byte> workBuffer) {
		ReadOnlySpan<byte> rest = MapEntity.SkipToNextEntity(data.Span, workBuffer);
		if (rest == null)
			return ReadOnlyMemory<byte>.Empty;
		return data[(data.Length - rest.Length)..];
	}

	static string TokenString(ReadOnlySpan<byte> token) => Encoding.ASCII.GetString(token[..MapEntity.StrLen(token)]);

	public static void MapEntity_ParseAllEntities(ReadOnlyMemory<byte> mapData, IMapEntityFilter? filter = null, bool activateEntities = false) {
		HierarchicalSpawnMapData_t[] spawnMapData = new HierarchicalSpawnMapData_t[Constants.NUM_ENT_ENTRIES];
		HierarchicalSpawn_t[] spawnList = new HierarchicalSpawn_t[Constants.NUM_ENT_ENTRIES];

		List<PointTemplate> pointTemplates = [];
		int numEntities = 0;

		Span<byte> tokenBuffer = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];

		// Allow the tools to spawn different things
		if (serverenginetools != null)
			mapData = serverenginetools.GetEntityData(mapData);

		//  Loop through all entities in the map data, creating each.
		Span<byte> token = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
		for (; true; mapData = SkipToNextEntity(mapData, tokenBuffer)) {
			// Parse the opening brace.
			mapData = ParseTokenAdvance(mapData, token);

			// Check to see if we've finished or not.
			if (mapData.IsEmpty)
				break;

			if (token[0] != (byte)'{') {
				Error($"MapEntity_ParseAllEntities: found {TokenString(token)} when expecting {{");
				continue;
			}

			// Parse the entity and add it to the spawn list.
			ReadOnlyMemory<byte> curMapData = mapData;
			mapData = ParseEntity(out BaseEntity? entity, mapData, filter);
			if (entity == null)
				continue;

			if (entity.IsTemplate()) {
				// It's a template entity. Squirrel away its keyvalue text so that we can
				// recreate the entity later via a spawner. mapData points at the '}'
				// so we must add one to include it in the string.
				Templates.Add(entity, curMapData.Span, (curMapData.Length - mapData.Length) + 2);

				// Remove the template entity so that it does not show up in FindEntityXXX searches.
				Util.Remove(entity);
				gEntList.CleanupDeleteList();
				continue;
			}

			// To
			if (entity is World) {
				entity.ParentName = null; // don't allow a parent on the first entity (worldspawn)

				Util.DispatchSpawn(entity);
				continue;
			}

			// TODO: CNodeEnt & CLight remove themselves immediately on Spawn(), so they should
			// dispatch their spawn now to free up the edict slot inside this loop. Those entity
			// classes are not ported yet, so they fall through to the regular queue for now.

			if (entity is NodeEnt ne) {
				if (ne.Spawn(curMapData) < 0)
					gEntList.CleanupDeleteList();
				continue;
			}

			// if (entity is Light light) {
			// 	if (Util.DispatchSpawn(light) < 0)
			// 		gEntList.CleanupDeleteList();
			// 	continue;
			// }

			// Build a list of all point_template's so we can spawn them before everything else
			if (entity is PointTemplate pt)
				pointTemplates.Add(pt);
			else {
				// Queue up this entity for spawning
				spawnList[numEntities].Entity = entity;
				spawnList[numEntities].Depth = 0;
				spawnList[numEntities].DeferredParentAttachment = null;
				spawnList[numEntities].DeferredParent = null;

				spawnMapData[numEntities].MapData = curMapData;
				spawnMapData[numEntities].MapDataLength = (curMapData.Length - mapData.Length) + 2;
				numEntities++;
			}
		}

		// Now loop through all our point_template entities and tell them to make templates of everything they're pointing to
		int templates = pointTemplates.Count;
		for (int i = 0; i < templates; i++) {
			PointTemplate pointTemplate = pointTemplates[i];

			// First, tell the Point template to Spawn
			if (Util.DispatchSpawn(pointTemplate) < 0) {
				Util.Remove(pointTemplate);
				gEntList.CleanupDeleteList();
				continue;
			}

			pointTemplate.StartBuildingTemplates();

			// Now go through all it's templates and turn the entities into templates
			int numTemplates = pointTemplate.GetNumTemplateEntities();
			for (int templateNm = 0; templateNm < numTemplates; templateNm++) {
				// Find it in the spawn list
				BaseEntity? entity = pointTemplate.GetTemplateEntity(templateNm);
				for (int iEntNum = 0; iEntNum < numEntities; iEntNum++) {
					if (spawnList[iEntNum].Entity == entity) {
						// Give the point_template the mapdata
						pointTemplate.AddTemplate(entity, spawnMapData[iEntNum].MapData, spawnMapData[iEntNum].MapDataLength);

						if (pointTemplate.ShouldRemoveTemplateEntities()) {
							// Remove the template entity so that it does not show up in FindEntityXXX searches.
							Util.Remove(entity);
							gEntList.CleanupDeleteList();

							// Remove the entity from the spawn list
							spawnList[iEntNum].Entity = null;
						}
						break;
					}
				}
			}

			pointTemplate.FinishBuildingTemplates();
		}

		SpawnHierarchicalList(numEntities, spawnList, activateEntities);
	}

	public static void MapEntity_PrecacheEntity(ReadOnlyMemory<byte> entData, int entDataSize) {
		EntityMapData mapData = new(entData, entDataSize);
		Span<byte> className = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];

		if (!mapData.ExtractValue("classname"u8, className))
			Error("classname missing from entity!\n");

		BaseEntity? entity = CreateEntityByName(TokenString(className));
		if (entity != null) {
			entity.ParseMapData(mapData);
			entity.Precache();
			Util.RemoveImmediate(entity);
		}
	}

	public static void FreeContainingEntity(Edict? ed) {
		if (ed != null) {
			BaseEntity? ent = BaseEntity.GetContainingEntity(ed);
			if (ent != null) {
				ed.SetEdict(null, false);
				BaseEntity.PhysicsRemoveTouchedList(ent);
				BaseEntity.PhysicsRemoveGroundList(ent);
				Util.RemoveImmediate(ent);
			}
		}
	}

	static string ExtractParentName(string parentName) {
		if (strstr(parentName, ",").IsEmpty)
			return parentName;

		throw new NotImplementedException();
	}

	static int ComputeSpawnHierarchyDepth_r(BaseEntity? entity) {
		if (entity == null)
			return 1;

		if (entity.ParentName == null)
			return 1;

		BaseEntity? parent = gEntList.FindEntityByName(null, ExtractParentName(entity.ParentName));
		if (parent == null)
			return 1;

		if (parent == entity) {
			Warning("LEVEL DESIGN ERROR: Entity %s is parented to itself!\n", entity.GetDebugName());
			return 1;
		}

		return 1 + ComputeSpawnHierarchyDepth_r(parent);
	}

	static void ComputeSpawnHierarchyDepth(int entities, HierarchicalSpawn_t[] spawnList) {
		for (int nEntity = 0; nEntity < entities; nEntity++) {
			BaseEntity? entity = spawnList[nEntity].Entity;
			if (entity != null && !entity.IsDormant())
				spawnList[nEntity].Depth = ComputeSpawnHierarchyDepth_r(entity);
			else
				spawnList[nEntity].Depth = 1;
		}
	}

	static void SpawnAllEntities(int numEntities, HierarchicalSpawn_t[] spawnList, bool activeEntities) {
		int nEntity;
		for (nEntity = 0; nEntity < numEntities; nEntity++) {
			BaseEntity? entity = spawnList[nEntity].Entity;

			if (spawnList[nEntity].DeferredParent != null) {
				BaseEntity parent = spawnList[nEntity].DeferredParent;
				int attachment = -1;
				BaseAnimating? anim = parent.GetBaseAnimating();
				// if (anim != null)
				// 	attachment = anim.LookupAttachment(spawnList[nEntity].DeferredParentAttachment);

				entity!.SetParent(parent, attachment);
			}

			if (entity != null) {
				if (Util.DispatchSpawn(entity) < 0) {
					for (int i = nEntity + 1; i < numEntities; i++) {
						// this is a child object that will be deleted now
						if (spawnList[i].Entity != null && spawnList[i].Entity!.IsMarkedForDeletion()) {
							spawnList[i].Entity = null;
						}
					}
					// Spawn failed.
					gEntList.CleanupDeleteList();
					// Remove the entity from the spawn list
					spawnList[nEntity].Entity = null;
				}
			}
		}

		if (activeEntities) {
			// bool asyncAnims = mdlcache.SetAsyncLoad(MDLCACHE_ANIMBLOCK, false);
			for (nEntity = 0; nEntity < numEntities; nEntity++) {
				BaseEntity? entity = spawnList[nEntity].Entity;
				entity?.Activate();
			}
			// mdlcache.SetAsyncLoad(MDLCACHE_ANIMBLOCK, asyncAnims);
		}
	}

	static void SortSpawnListByHierarchy(int entities, HierarchicalSpawn_t[] spawnList) {

	}

	static void SetupParentsForSpawnList(int entities, HierarchicalSpawn_t[] spawnList) {
		int nEntity;
		for (nEntity = entities - 1; nEntity >= 0; nEntity--) {
			BaseEntity? entity = spawnList[nEntity].Entity;
			if (entity != null) {
				if (entity.ParentName != null && entity.ParentName.Contains(',')) {
					nexttoken(out ReadOnlySpan<char> token, entity.ParentName, ',', out ReadOnlySpan<char> attachmentName);
					entity.ParentName = new(token);
					BaseEntity? parent = gEntList.FindEntityByName(null, entity.ParentName);

					spawnList[nEntity].DeferredParent = parent!;
					spawnList[nEntity].DeferredParentAttachment = new(attachmentName);
				}
				else {
					BaseEntity? parent = gEntList.FindEntityByName(null, entity.ParentName);

					if (parent != null && parent.Edict() != null)
						entity.SetParent(parent);
				}
			}
		}
	}

	internal static void SpawnHierarchicalList(int entities, HierarchicalSpawn_t[] spawnList, bool activateEntities) {
		// Compute the hierarchical depth of all entities hierarchically attached
		ComputeSpawnHierarchyDepth(entities, spawnList);

		// Sort the entities (other than the world) by hierarchy depth, in order to spawn them in
		// that order. This insures that each entity's parent spawns before it does so that
		// it can properly set up anything that relies on hierarchy.
		SortSpawnListByHierarchy(entities, spawnList);

		// save off entity positions if in edit mode
		// if (engine.IsInEditMode()) // TODO
		// 	RememberInitialEntityPositions(entities, spawnList);

		// Set up entity movement hierarchy in reverse hierarchy depth order. This allows each entity
		// to use its parent's world spawn origin to calculate its local origin.
		SetupParentsForSpawnList(entities, spawnList);

		// Spawn all the entities in hierarchy depth order so that parents spawn before their children.
		SpawnAllEntities(entities, spawnList, activateEntities);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Takes a block of character data as the input
	// Input  : entity - Receives the newly constructed entity, null on failure.
	//			entData - Data block to parse to extract entity keys.
	// Output : Returns the current position in the entity data block.
	//-----------------------------------------------------------------------------
	public static ReadOnlyMemory<byte> ParseEntity(out BaseEntity? entity, ReadOnlyMemory<byte> entDataMem, IMapEntityFilter? filter) {
		EntityMapData entData = new(entDataMem);
		Span<byte> className = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];

		if (!entData.ExtractValue("classname"u8, className))
			Error("classname missing from entity!\n");

		ReadOnlySpan<char> classNameStr = Encoding.ASCII.GetString(className[..MapEntity.StrLen(className)]);

		entity = null;
		if (filter == null || filter.ShouldCreateEntity(classNameStr)) {

			// Construct via the LINK_ENTITY_TO_CLASS factory.
			if (filter != null)
				entity = filter.CreateNextEntity(classNameStr);
			else
				entity = CreateEntityByName(classNameStr);

			// Set up keyvalues.
			if (entity != null) 
				entity.ParseMapData(entData);
			else
				Warning($"Can't init {classNameStr}\n");
		}
		else {
			// Just skip past all the keys.
			Span<byte> keyName = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
			Span<byte> value = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
			if (entData.GetFirstKey(keyName, value)) {
				do {
				}
				while (entData.GetNextKey(keyName, value));
			}
		}

		// Return the current parser position in the data block
		return entData.CurrentBufferPosition();
	}
}
