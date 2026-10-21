using Source;
using Source.Common.Bitbuffers;
using Source.Common.Hashing;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Constants;

namespace Game.Shared;

public struct UserCmd
{
	public static bool IsNull(ref UserCmd cmd) => Unsafe.IsNullRef(ref cmd);
	public static ref UserCmd NULL => ref Unsafe.NullRef<UserCmd>();
	public void Reset() {
		CommandNumber = 0;
		TickCount = 0;
		ViewAngles = new();
		ForwardMove = 0.0f;
		SideMove = 0.0f;
		UpMove = 0.0f;
		Buttons = 0;
		Impulse = 0;
		WeaponSelect = 0;
		WeaponSubtype = 0;
		RandomSeed = 0;
#if GAME_DLL
		ServerRandomSeed = 0;
#endif
		MouseDeltaX = 0;
		MouseDeltaY = 0;

		HasBeenPredicted = false;

		for (int i = 0; i < MAX_BUTTONS_PRESSED; i++)
			ButtonsPressed[i] = 0;

		ScrollWheelSpeed = 0;
		WorldClicking = false;
		WorldClickDirection = new(0);
		IsTyping = false;
		for (int i = 0; i < MAX_MOTION_SENSOR_POSITIONS; i++)
			MotionSensorPositions[i] = new(0);
		Forced = false;
	}

	public void MakeInert() {
		ViewAngles = vec3_angle;
		ForwardMove = 0.0f;
		SideMove = 0.0f;
		UpMove = 0.0f;
		Buttons = 0;
		Impulse = 0;
	}

	public const int WEAPON_SUBTYPE_BITS = 6;

	/// <summary>
	/// For matching server and client commands for debugging
	/// </summary>
	public int CommandNumber;

	/// <summary>
	/// the tick the client created this command
	/// </summary>
	public int TickCount;

	/// <summary>
	/// Player instantaneous view angles.
	/// </summary>
	public QAngle ViewAngles;



	/// <summary>
	/// forward velocity.
	/// </summary>
	public float ForwardMove;
	/// <summary>
	///  sideways velocity.
	/// </summary>
	public float SideMove;
	/// <summary>
	///  upward velocity.
	/// </summary>
	public float UpMove;

	/// <summary>
	/// Attack button states
	/// </summary>
	public InButtons Buttons;

	/// <summary>
	/// Impulse command issued.
	/// </summary>
	public byte Impulse;

	/// <summary>
	/// Current weapon id
	/// </summary>
	public int WeaponSelect;
	public int WeaponSubtype;

	/// <summary>
	/// For shared random functions
	/// </summary>
	public int RandomSeed;

#if GAME_DLL
	/// <summary>
	/// Only the server populates this seed
	/// </summary>
	public int ServerRandomSeed;
#endif

	/// <summary>
	/// mouse accum in x from create move
	/// </summary>
	public short MouseDeltaX;
	/// <summary>
	/// mouse accum in y from create move
	/// </summary>
	public short MouseDeltaY;

	/// <summary>
	/// Client only, tracks whether we've predicted this command at least once
	/// </summary>
	public bool HasBeenPredicted;

	public const int MAX_BUTTONS_PRESSED = 5;
	/// <summary>
	/// Holds current buttons pressed, and sends to server used for PlayerButtonDown and other hooks serverside.
	/// </summary>
	public Source.InlineArray5<byte> ButtonsPressed;

	/// <summary>
	/// Scroll wheel speed.
	/// </summary>
	public sbyte ScrollWheelSpeed;

	/// <summary>
	/// Currently world clicking? Used for context menu aiming.
	/// </summary>
	public bool WorldClicking;

	/// <summary>
	/// World click direction.
	/// </summary>
	public Vector3 WorldClickDirection;

	/// <summary>
	/// Performs the hand -> ear animation
	/// </summary>
	public bool IsTyping;

	public const int MAX_MOTION_SENSOR_POSITIONS = 20;
	/// <summary>
	/// Kinect stuff
	/// </summary>
	public InlineArray20<Vector3> MotionSensorPositions;

	/// <summary>
	/// Gmod wiki
	/// </summary>
	public bool Forced;

	public static void ReadUsercmd(bf_read buf, ref UserCmd move, ref UserCmd from) {
		move = from;

		if (buf.ReadOneBit() != 0)
			move.CommandNumber = (int)buf.ReadUBitLong(32);
		else
			move.CommandNumber = from.CommandNumber + 1;

		if (buf.ReadOneBit() != 0)
			move.TickCount = (int)buf.ReadUBitLong(32);
		else
			move.TickCount = from.TickCount + 1;

		if (buf.ReadOneBit() != 0)
			move.ViewAngles.X = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.ViewAngles.Y = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.ViewAngles.Z = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.ForwardMove = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.SideMove = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.UpMove = buf.ReadFloat();

		if (buf.ReadOneBit() != 0)
			move.Buttons = (InButtons)buf.ReadUBitLong(32);

		if (buf.ReadOneBit() != 0)
			move.Impulse = (byte)buf.ReadUBitLong(8);

		if (buf.ReadOneBit() != 0) {
			move.WeaponSelect = (int)buf.ReadUBitLong(MAX_EDICT_BITS);
			if (buf.ReadOneBit() != 0)
				move.WeaponSubtype = (int)buf.ReadUBitLong(WEAPON_SUBTYPE_BITS);
		}

		move.RandomSeed = (int)(MD5Value.PseudoRandom((uint)move.CommandNumber) & 0x7fffffff);

		if (buf.ReadOneBit() != 0)
			move.MouseDeltaX = buf.ReadShort();

		if (buf.ReadOneBit() != 0)
			move.MouseDeltaY = buf.ReadShort();

		if (buf.ReadOneBit() != 0) {
			for (int i = 0; i < MAX_BUTTONS_PRESSED; i++) {
				move.ButtonsPressed[i] = (byte)buf.ReadUBitLong(8);
				if (move.ButtonsPressed[i] == 0)
					break;
			}
		}

		if (buf.ReadOneBit() != 0)
			move.ScrollWheelSpeed = (sbyte)buf.ReadSBitLong(8);

		if (buf.ReadOneBit() != 0)
			move.WorldClicking = buf.ReadOneBit() != 0;

		if (buf.ReadOneBit() != 0)
			move.WorldClickDirection = buf.ReadBitVec3Normal();

		move.IsTyping = buf.ReadOneBit() != 0;

		if (buf.ReadOneBit() != 0) {
			for (int i = 0; i < MAX_MOTION_SENSOR_POSITIONS; i++)
				move.MotionSensorPositions[i] = buf.ReadBitVec3Coord();
		}

		move.Forced = buf.ReadOneBit() != 0;
	}

	static bool HasChanged<T>(Source.InlineArray5<T> from, Source.InlineArray5<T> to) where T : IEquatable<T> {
		for (int i = 0; i < 5; i++) {
			if (!from[i].Equals(to[i]))
				return true;
		}

		return false;
	}
	static bool HasChanged<T>(InlineArray20<T> from, InlineArray20<T> to) where T : IEquatable<T> {
		for (int i = 0; i < 20; i++) {
			if (!from[i].Equals(to[i]))
				return true;
		}

		return false;
	}

	public static void WriteUsercmd(bf_write buf, in UserCmd to, in UserCmd from) {
		if (to.CommandNumber != from.CommandNumber + 1) {
			buf.WriteOneBit(1);
			buf.WriteUBitLong((uint)to.CommandNumber, 32);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.TickCount != from.TickCount + 1) {
			buf.WriteOneBit(1);
			buf.WriteUBitLong((uint)to.TickCount, 32);
		}
		else {
			buf.WriteOneBit(0);
		}


		if (to.ViewAngles[0] != from.ViewAngles[0]) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.ViewAngles[0]);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.ViewAngles[1] != from.ViewAngles[1]) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.ViewAngles[1]);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.ViewAngles[2] != from.ViewAngles[2]) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.ViewAngles[2]);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.ForwardMove != from.ForwardMove) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.ForwardMove);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.SideMove != from.SideMove) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.SideMove);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.UpMove != from.UpMove) {
			buf.WriteOneBit(1);
			buf.WriteFloat(to.UpMove);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.Buttons != from.Buttons) {
			buf.WriteOneBit(1);
			buf.WriteUBitLong((uint)to.Buttons, 32);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.Impulse != from.Impulse) {
			buf.WriteOneBit(1);
			buf.WriteUBitLong(to.Impulse, 8);
		}
		else {
			buf.WriteOneBit(0);
		}


		if (to.WeaponSelect != from.WeaponSelect) {
			buf.WriteOneBit(1);
			buf.WriteUBitLong((uint)to.WeaponSelect, MAX_EDICT_BITS);

			if (to.WeaponSubtype != from.WeaponSubtype) {
				buf.WriteOneBit(1);
				buf.WriteUBitLong((uint)to.WeaponSubtype, WEAPON_SUBTYPE_BITS);
			}
			else {
				buf.WriteOneBit(0);
			}
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.MouseDeltaX != from.MouseDeltaX) {
			buf.WriteOneBit(1);
			buf.WriteShort(to.MouseDeltaX);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.MouseDeltaY != from.MouseDeltaY) {
			buf.WriteOneBit(1);
			buf.WriteShort(to.MouseDeltaY);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (HasChanged(to.ButtonsPressed, from.ButtonsPressed)) {
			buf.WriteOneBit(1);
			for (int i = 0; i < 5; i++) {
				if (to.ButtonsPressed[i] == 0)
					break;
				buf.WriteUBitLong(to.ButtonsPressed[i], 8);
			}
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.ScrollWheelSpeed != from.ScrollWheelSpeed) {
			buf.WriteOneBit(1);
			buf.WriteSBitLong(to.ScrollWheelSpeed, 8);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.WorldClicking != from.WorldClicking) {
			buf.WriteOneBit(1);
			buf.WriteOneBit(to.WorldClicking ? 1 : 0);
		}
		else {
			buf.WriteOneBit(0);
		}

		if (to.WorldClickDirection != from.WorldClickDirection) {
			buf.WriteOneBit(1);
			buf.WriteBitVec3Normal(to.WorldClickDirection);
		}
		else {
			buf.WriteOneBit(0);
		}

		buf.WriteOneBit(to.IsTyping ? 1 : 0);

		if (HasChanged(to.MotionSensorPositions, from.MotionSensorPositions)) {
			buf.WriteOneBit(1);
			for (int i = 0; i < MAX_MOTION_SENSOR_POSITIONS; i++) {
				buf.WriteBitVec3Coord(to.MotionSensorPositions[i]);
			}
		}
		else {
			buf.WriteOneBit(0);
		}

		buf.WriteOneBit(to.Forced ? 1 : 0);
	}
}
