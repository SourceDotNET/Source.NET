global using static Source.Engine.EdictEngineGlobals;

using Source.Common.Commands;
using Source.Common.Engine;

namespace Source.Engine;

public static class EdictEngineGlobals
{
	public static Edict EDICT_NUM(int n) {
		if ((uint)n >= (uint)sv.MaxEdicts)
			Sys.Error($"EDICT_NUM: bad number {n}");
		return sv.Edicts![n];
	}

	public static int NUM_FOR_EDICT(Edict e) {
		if (sv.Edicts![e.EdictIndex] == e) // NOTE: old server.dll may stomp m_EdictIndex
			return e.EdictIndex;
		int index = sv.Edicts.IndexOf(e); // This is gross but it works for now. Fixme, probably an easy optimization target
		if (index == -1)
			Sys.Error("NUM_FOR_EDICT: bad edict");
		return index;
	}

}

public class ED
{
	const float EDICT_FREETIME = 1.0f;

	static readonly ConVar sv_useexplicitdelete = new("1", FCvar.DevelopmentOnly, "Explicitly delete dormant client entities caused by AllowImmediateReuse().");
	static readonly ConVar sv_lowEdicthreshold = new("8", FCvar.None, "When only this many edicts are free, take the action specified by sv_lowedict_action.", 0, Constants.MAX_EDICTS);
	static readonly ConVar sv_lowedict_action = new("0", FCvar.None, "0 - no action, 1 - warn to log file, 2 - attempt to restart the game, if applicable, 3 - restart the map, 4 - go to the next map in the map cycle, 5 - spew all edicts.", 0, 5);
	static MaxEdictsBitSet FreeEdicts;

	public static Edict? Alloc(int forceEdictIndex) {
		if (forceEdictIndex >= 0) {
			if (forceEdictIndex >= sv.NumEdicts) {
				Warning("ED_Alloc( %d ) - invalid edict index specified.", forceEdictIndex);
				return null;
			}

			Edict e = sv.Edicts![forceEdictIndex];
			if (e.IsFree()) {
				Assert(forceEdictIndex == e.EdictIndex);
				--sv.FreeEdicts;
				Assert(FreeEdicts.IsBitSet(forceEdictIndex));
				FreeEdicts.Clear(forceEdictIndex);
				ClearEdict(e);
				return e;
			}
			else
				return null;
		}

		// Check the free list first.
		int bit = -1;
		Edict edict;
		for (; ; )
		{
			bit = FreeEdicts.FindNextSetBit(bit + 1);
			if (bit < 0)
				break;

			edict = sv.Edicts![bit];

			// If this assert goes off, someone most likely called pedict.ClearFree() and not ED_ClearFreeFlag()?
			Assert(edict.IsFree());
			Assert(bit == edict.EdictIndex);
			if ((edict.FreeTime < 2) || (sv.GetTime() - edict.FreeTime >= EDICT_FREETIME)) {
				// If we have no freetime, we've had AllowImmediateReuse() called. We need
				// to explicitly delete this old entity.
				if (edict.FreeTime == 0 && sv_useexplicitdelete.GetBool()) {
					//Warning("ADDING SLOT to snapshot: %d\n", i );
					sv.FrameSnapshotManager.AddExplicitDelete(bit);
				}

				--sv.FreeEdicts;
				FreeEdicts.Clear(edict.EdictIndex);
				ClearEdict(edict);
				return edict;
			}
		}

		// Allocate a new edict.
		if (sv.NumEdicts >= sv.MaxEdicts) {
			AssertMsg(false, "Can't allocate edict");

			SpewEdicts(); // Log the entities we have before we die

			if (sv.MaxEdicts == 0)
				Sys.Error("ED_Alloc: No edicts yet");
			Sys.Error("ED_Alloc: no free edicts");
		}

		// Do this before clearing since clear now needs to call back into the edict to deduce the index so can get the changeinfo data in the parallel structure
		edict = sv.Edicts![sv.NumEdicts++];

		// We should not be in the free list...
		Assert(!FreeEdicts.IsBitSet(edict.EdictIndex));
		ClearEdict(edict);

		if (sv_lowedict_action.GetInt() > 0 && sv.NumEdicts >= sv.MaxEdicts - sv_lowEdicthreshold.GetInt()) {
			int edictsRemaining = sv.MaxEdicts - sv.NumEdicts;
			// Log.Printf("Warning: free edicts below threshold. %i free edict%s remaining.\n", edictsRemaining, edictsRemaining == 1 ? "" : "s"); todo

			switch (sv_lowedict_action.GetInt()) {
				case 2:
					// restart the game
					{
						ConVarRef mp_restartgame_immediate = new("mp_restartgame_immediate");
						if (mp_restartgame_immediate.IsValid()) {
							mp_restartgame_immediate.SetValue(1);
						}
						else {
							ConVarRef mp_restartgame = new("mp_restartgame");
							if (mp_restartgame.IsValid())
								mp_restartgame.SetValue(1);
						}
					}
					break;
				case 3:
					// restart the map
					engine.ChangeLevel(sv.GetMapName(), null);
					break;
				case 4:
					// go to the next map
					engine.ServerCommand("changelevel_next\n");
					break;
				case 5:
					// spew all edicts
					SpewEdicts();
					break;
			}
		}

		return edict;
	}

	private static void SpewEdicts() {
		Dictionary<string, int> mapEnts = [];

		int edictNum = 1;
		for (int i = 0; i < sv.NumEdicts; ++i) {
			Edict e = sv.Edicts![i];
			++edictNum;
			string className = new(e.GetClassName());
			mapEnts[className] = mapEnts.GetValueOrDefault(className) + 1;
		}

		List<KeyValuePair<string, int>> vecEnts = [.. mapEnts];
		vecEnts.Sort((p1, p2) => p1.Value - p2.Value);

		// todo: g_Log.Printf
		Msg("Spewing edict counts:\n");
		foreach (KeyValuePair<string, int> ent in vecEnts)
			Msg($"({ent.Value / (float)edictNum * 100.0f:F2}%) {ent.Value}\t{ent.Key}\n");
		Msg($"Total edicts: {edictNum}\n");
	}

	public static void ClearEdict(Edict e) {
		e.ClearFree();
		e.ClearStateChanged();
		e.SetChangeInfoSerialNumber(0);

		// serverGameEnts.FreeContainingEntity(e);
		e.InitializeEntityDLLFields();
		e.NetworkSerialNumber = -1;  // must be filled by game.dll
	}

	public void ClearFreeEdictList() {
		FreeEdicts.ClearAll();
	}

	public void ClearFreeFlag(Edict e) {
		e.ClearFree();
		FreeEdicts.Clear(e.EdictIndex);
	}

	internal static void Free(Edict ed) {
		if (ed.IsFree())
			return;

		int idx = Array.IndexOf(sv.Edicts!, ed);
		if (idx >= 1 && idx <= sv.GetMaxClients())
			return;

		SV.ServerGameEnts?.FreeContainingEntity(ed);

		ed.SetFree();
		ed.FreeTime = sv.GetTime();

		++sv.FreeEdicts;
		Assert(!FreeEdicts.IsBitSet(ed.EdictIndex));
		FreeEdicts.Set(ed.EdictIndex);

		ed.NetworkSerialNumber++;
	}

	internal static void AllowImmediateReuse() {
		for (int i = sv.GetMaxClients() + 1; i < sv.NumEdicts; i++) {
			Edict edict = sv.Edicts![i];
			if (edict.IsFree())
				edict.FreeTime = 0;
		}
	}
}
