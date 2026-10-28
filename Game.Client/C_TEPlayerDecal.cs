using Source.Common;
using Source;
using Source.Common.Commands;
using Source.Common.Client;
using Source.Common.MaterialSystem;

using Game.Shared;

using System.Numerics;
using System.Runtime.InteropServices;
namespace Game.Client;

[NetworkName("CTEPlayerDecal")]
public partial class C_TEPlayerDecal : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Player)) { Bits = 7, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nPlayer")]
	public int Player;

	public static readonly ConVar cl_playerspraydisable = new( "cl_playerspraydisable", "0", FCvar.ClientDLL | FCvar.Archive, "Disable player sprays." );


	public override void PostDataUpdate(DataUpdateType updateType) {
		if (!EffectsClient.r_decals.GetBool())
			return;

		LocalPlayerFilter filter = new();
		TE_PlayerDecal(filter, 0.0f, in Origin, Player, Entity);
	}
}

public static partial class TempEnts
{
	public static IMaterial? CreateTempMaterialForPlayerLogo(int playerIndex, ref PlayerInfo info, Span<char> texName) {
		if (info.CustomFiles[0] == 0)
			return null;

		Span<char> logoIndex = stackalloc char[11];
		playerIndex.TryFormat(logoIndex, out _, "D2");
		Span<char> logoName = stackalloc char[64];
		sprintf(logoName, "decals/playerlogo%s").S(logoIndex);

		IMaterial logo = materials.FindMaterial(logoName.SliceNullTerminatedString(), MaterialDefines.TEXTURE_GROUP_DECAL);
		if (logo.IsErrorMaterial())
			return null;

		Span<char> logoHex = stackalloc char[16];
		binarytohex(MemoryMarshal.AsBytes(info.CustomFiles[..1]), logoHex);

		sprintf(texName, "temp/%s").S(logoHex);
		Span<char> fullTexName = stackalloc char[512];
		sprintf(fullTexName, "materials/temp/%s.vtf").S(logoHex);

		if (!filesystem.FileExists(fullTexName.SliceNullTerminatedString())) {
			Span<char> custName = stackalloc char[512];
			sprintf(custName, "download/user_custom/%s%s/%s.dat").S(logoHex[..1]).S(logoHex[1..2]).S(logoHex);
			if (!filesystem.FileExists(custName.SliceNullTerminatedString()))
				return null;

			if (!engine.CopyLocalFile(custName.SliceNullTerminatedString(), fullTexName.SliceNullTerminatedString()))
				return null;
		}

		return logo;
	}

	public static void TE_PlayerDecal(IRecipientFilter filter, float delay, in Vector3 pos, int player, int entity) {
		if (C_TEPlayerDecal.cl_playerspraydisable.GetBool())
			return;

		C_BaseEntity? ent = cl_entitylist.GetEnt(entity);
		if (ent == null)
			return;

		engine.GetPlayerInfo(player, out PlayerInfo info);

		Span<char> texname = stackalloc char[512];
		IMaterial? logo = CreateTempMaterialForPlayerLogo(player, ref info, texname);
		if (logo == null)
			return;

		ITexture texture = materials.FindTexture(texname.SliceNullTerminatedString(), MaterialDefines.TEXTURE_GROUP_DECAL);
		if (ITexture.IsError(texture))
			return;

		IMaterialVar? matVar = logo.FindVar("$basetexture", out bool found);
		if (found && matVar != null) {
			if (matVar.GetTextureValue() != texture) {
				matVar.SetTextureValue(texture);
				logo.RefreshPreservingMaterialVars();
			}
		}

		Color rgbaColor = new(255, 255, 255, 255);
		effects.PlayerDecalShoot(logo, player, entity, ent.GetModel(), ent.GetAbsOrigin(), ent.GetAbsAngles(), in pos, null, 0, in rgbaColor);
	}
}
