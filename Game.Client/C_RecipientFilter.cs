using Game.Shared;

using Source;
using Source.Common;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Client;

public class C_RecipientFilter : IRecipientFilter
{
	static IPredictionSystem g_RecipientFilterPredictionSystem = new();

	public C_RecipientFilter() {
		Reset();
	}

	public virtual bool IsReliable() {
		return Reliable;
	}

	public virtual int GetRecipientCount() {
		return Recipients.Count;
	}

	public virtual int GetRecipientIndex(int slot) {
		if (slot < 0 || slot >= GetRecipientCount())
			return -1;

		return Recipients[slot];
	}

	public virtual bool IsInitMessage() => false;

	public void CopyFrom(C_RecipientFilter src) {
		Reliable = src.IsReliable();
		InitMessage = src.IsInitMessage();

		UsingPredictionRules = src.IsUsingPredictionRules();
		bIgnorePredictionCull = src.IgnorePredictionCull();

		int c = src.GetRecipientCount();
		for (int i = 0; i < c; ++i)
			Recipients.Add(src.GetRecipientIndex(i));
	}

	public void Reset() {
		Reliable = false;
		Recipients.Clear();
		UsingPredictionRules = false;
		bIgnorePredictionCull = false;
	}

	public void MakeReliable() {
		Reliable = true;
	}

	public void AddAllPlayers(){
		if (C_BasePlayer.GetLocalPlayer() == null)
			return;

		Recipients.Clear();
		AddRecipient(C_BasePlayer.GetLocalPlayer()!);
	}
	public void AddRecipientsByPVS(in Vector3 origin) {
		AddAllPlayers();
	}
	public void AddRecipientsByPAS(in Vector3 origin) {
		AddAllPlayers();
	}
	public void AddRecipient(C_BasePlayer? player) {
		Assert(player != null);

		if (player == null)
			return;

		int index = player.Index;

		// If we're predicting and this is not the first time we've predicted this sound
		//  then don't send it to the local player again.
		if (UsingPredictionRules) {
			Assert(player == C_BasePlayer.GetLocalPlayer());
			Assert(prediction.InPrediction());

			// Only add local player if this is the first time doing prediction
			if (!g_RecipientFilterPredictionSystem.CanPredict()) 
				return;
		}

		// Already in list
		if (Recipients.IndexOf(index) != -1)
			return;

		// this is a client side filter, only add the local player
		if (!player.IsLocalPlayer())
			return;

		Recipients.Add(index);
	}
	public void RemoveRecipient(C_BasePlayer? player) {
		if (player == null)
			return;

		int index = player.Index;

		// Remove it if it's in the list
		Recipients.Remove(index);
	}
	public void AddRecipientsByTeam(C_Team team) {
		AddAllPlayers();
	}
	public void RemoveRecipientsByTeam(C_Team team) {
		Assert(false);
	}

	public void UsePredictionRules() {
		if (UsingPredictionRules)
			return;

		if (!prediction.InPrediction()) 
			return;
		

		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local == null) {
			Assert(false);
			return;
		}

		UsingPredictionRules = true;

		// Cull list now, if needed
		int c = GetRecipientCount();
		if (c == 0)
			return;

		if (!g_RecipientFilterPredictionSystem.CanPredict()) 
			RemoveRecipient(local);
	}
	public bool IsUsingPredictionRules() => UsingPredictionRules;

	public bool IgnorePredictionCull() => bIgnorePredictionCull;
	public void SetIgnorePredictionCull(bool ignore) => bIgnorePredictionCull = ignore;
	public void AddPlayersFromBitMask(in PlayerBitSet playerbits) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();

		if (player == null)
			return;

		// only add the local player on client side
		if (0 == playerbits[player.Index])
			return;

		AddRecipient(player);
	}

	bool Reliable;
	bool InitMessage;
	protected readonly List<int> Recipients = [];
	// If using prediction rules, the filter itself suppresses local player
	bool UsingPredictionRules;
	// If ignoring prediction cull, then external systems can determine
	//  whether this is a special case where culling should not occur
	bool bIgnorePredictionCull;
}

public class SingleUserRecipientFilter : C_RecipientFilter
{
	public SingleUserRecipientFilter(C_BasePlayer? player) : base() {
		AddRecipient(player);
	}
}


public class BroadcastRecipientFilter : C_RecipientFilter
{
	public BroadcastRecipientFilter() : base() {
		AddAllPlayers();
	}
}


public class ReliableBroadcastRecipientFilter : BroadcastRecipientFilter
{
	public ReliableBroadcastRecipientFilter() : base() {
		MakeReliable();
	}
}


public class PASFilter : C_RecipientFilter
{
	public PASFilter(in Vector3 origin) : base() {
		AddRecipientsByPVS(in origin);
	}
}


public class PASAttenuationFilter : PASFilter
{
	public PASAttenuationFilter(C_BaseEntity entity, float attenuation = ATTN_NORM ) :		base(entity.GetAbsOrigin() ) {	}
	public PASAttenuationFilter(in Vector3 origin, float attenuation = ATTN_NORM ) :		base(origin ) {	}
	public PASAttenuationFilter(C_BaseEntity entity, ReadOnlySpan<char> s ) :		base(entity.GetAbsOrigin() ) {	}
	public PASAttenuationFilter(in Vector3 origin, ReadOnlySpan<char> s) :		base(origin ) {	}
	public PASAttenuationFilter(C_BaseEntity entity, ReadOnlySpan<char> s, ref HSOUNDSCRIPTHANDLE h ) :		base(entity.GetAbsOrigin() ) {	}
	public PASAttenuationFilter(in Vector3 origin, ReadOnlySpan<char> s, ref HSOUNDSCRIPTHANDLE h ) :		base(origin ) {	}
}

public class PVSFilter : C_RecipientFilter
{
	public PVSFilter(in Vector3 origin) : base() {
		AddRecipientsByPVS(in origin);
	}
}


public class LocalPlayerFilter : C_RecipientFilter
{
	public LocalPlayerFilter() : base() {
		if (C_BasePlayer.GetLocalPlayer() != null)
			AddRecipient(C_BasePlayer.GetLocalPlayer());
	}
}


public class UIFilter : C_RecipientFilter
{
	public UIFilter() : base() {
		Recipients.Add(1);
	}
}
