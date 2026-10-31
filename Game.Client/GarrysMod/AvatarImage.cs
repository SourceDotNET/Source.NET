using Source;
using Source.Common.GUI;
using Source.GUI.Controls;

using Steamworks;

namespace Game.Client.GarrysMod;

public class AvatarImage : Panel
{
	static readonly List<TextureID>[] FreeTextures = [[], [], []];

	TextureID TextureID;
	ulong SteamID;
	int Size;
	readonly IImage? DefaultImage;

	readonly Callback<AvatarImageLoaded_t> AvatarImageLoaded;
	readonly Callback<PersonaStateChange_t> PersonaStateChange;

	public AvatarImage(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SteamID = 0;
		Size = 0;
		AvatarImageLoaded = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
		PersonaStateChange = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
		TextureID = TextureID.INVALID;
		DefaultImage = SchemeManager.GetImage("avatar_default", true);
	}

	public override void Dispose() {
		ClearTexture();
		PersonaStateChange.Dispose();
		AvatarImageLoaded.Dispose();
		base.Dispose();
	}

	void OnAvatarImageLoaded(AvatarImageLoaded_t info) {
		if (info.m_steamID.m_SteamID == SteamID)
			LoadAvatar();
	}

	void OnPersonaStateChange(PersonaStateChange_t info) {
		if (info.m_ulSteamID == SteamID && (info.m_nChangeFlags & EPersonaChange.k_EPersonaChangeAvatar) != 0)
			LoadAvatar();
	}

	void ClearTexture() {
		SteamID = 0;
		if (TextureID == TextureID.INVALID) {
			TextureID = TextureID.INVALID;
			return;
		}

		FreeTextures[Size].Add(TextureID);
		TextureID = TextureID.INVALID;
	}

	void LoadAvatar() {
		CSteamID steamID = new(SteamID);
		if (SteamFriends.RequestUserInformation(steamID, false))
			return;

		int avatar;
		if (Size == 0)
			avatar = SteamFriends.GetSmallFriendAvatar(steamID);
		else if (Size == 1)
			avatar = SteamFriends.GetMediumFriendAvatar(steamID);
		else if (Size == 2)
			avatar = SteamFriends.GetLargeFriendAvatar(steamID);
		else
			return;

		if ((uint)(avatar + 1) <= 1)
			return;

		if (!SteamUtils.GetImageSize(avatar, out uint wide, out uint tall))
			return;

		int size = (int)(wide * tall * 4);
		byte[] rgba = new byte[size];
		if (!SteamUtils.GetImageRGBA(avatar, rgba, size))
			return;

		if (TextureID == TextureID.INVALID) {
			List<TextureID> free = FreeTextures[Size];
			if (free.Count < 1)
				TextureID = Surface.CreateNewTextureID(true);
			else {
				TextureID = free[^1];
				free.RemoveAt(free.Count - 1);
			}
		}

		Surface.DrawSetTextureRGBA(TextureID, rgba, (int)wide, (int)tall, 0, true);
	}

	public void SetSteamID(ulong steamID, int size) {
		if (size < 0)
			size = 0;
		else if (2 < size)
			size = 2;

		if (SteamID != steamID || size != Size) {
			if (size != Size)
				ClearTexture();
			SteamID = steamID;
			Size = size;
			LoadAvatar();
		}
	}

	public void SetPlayer(C_BasePlayer? player, int size) {
		if (player == null || !player.GetSteamID(out CSteamID id)) {
			ClearTexture();
			return;
		}

		ulong steamID = id.m_SteamID;
		int clamped = 0;
		if (-1 < size) {
			clamped = size;
			if (2 < size)
				clamped = 2;
		}

		if (SteamID == steamID && clamped == Size)
			return;

		if (clamped != Size)
			ClearTexture();

		SteamID = steamID;
		Size = clamped;
		LoadAvatar();
	}

	public override void Paint() {
		if (TextureID == TextureID.INVALID) {
			DefaultImage!.SetSize(GetWide(), GetTall());
			DefaultImage.Paint();
			return;
		}

		Surface.DrawSetTexture(TextureID);
		Surface.DrawSetColor(new Color(255, 255, 255, 255));
		Surface.DrawTexturedRect(0, 0, GetWide(), GetTall());
	}
}
