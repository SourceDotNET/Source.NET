using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;

public partial class Input
{
	ConVar cam_command = new("0", FCvar.Cheat);
	ConVar cam_snapto = new("0", FCvar.Archive | FCvar.Cheat);
	ConVar cam_ideallag = new("4.0", FCvar.Archive | FCvar.Cheat, "Amount of lag used when matching offset to ideal angles in thirdperson view");
	ConVar cam_idealdelta = new("4.0", FCvar.Archive | FCvar.Cheat, "Controls the speed when matching offset to ideal angles in thirdperson view");
	ConVar cam_idealyaw = new("0", FCvar.Archive | FCvar.Cheat);
	ConVar cam_idealpitch = new("0", FCvar.Archive | FCvar.Cheat);
	static ConVar cam_idealdist = new("150", FCvar.Archive | FCvar.Cheat);
	static ConVar cam_idealdistright = new("0", FCvar.Archive | FCvar.Cheat);
	static ConVar cam_idealdistup = new("0", FCvar.Archive | FCvar.Cheat);
	ConVar cam_collision = new("1", FCvar.Archive | FCvar.Cheat, "When in thirdperson and cam_collision is set to 1, an attempt is made to keep the camera from passing though walls.");
	ConVar cam_showangles = new("0", FCvar.Cheat, "When in thirdperson, print viewangles/idealangles/cameraoffsets to the console.");
	ConVar c_maxpitch = new("90", FCvar.Archive | FCvar.Cheat);
	ConVar c_minpitch = new("0", FCvar.Archive | FCvar.Cheat);
	ConVar c_maxyaw = new("135", FCvar.Archive | FCvar.Cheat);
	ConVar c_minyaw = new("-135", FCvar.Archive | FCvar.Cheat);
	ConVar c_maxdistance = new("200", FCvar.Archive | FCvar.Cheat);
	ConVar c_mindistance = new("30", FCvar.Archive | FCvar.Cheat);
	ConVar c_orthowidth = new("100", FCvar.Archive | FCvar.Cheat);
	ConVar c_orthoheight = new("100", FCvar.Archive | FCvar.Cheat);

	public float CAM_CapPitch(float val) => val;
	public float CAM_CapYaw(float val) => val;

	[ConCommand("thirdperson", "Switch to thirdperson camera.", FCvar.Cheat)]
	static void thirdperson() {
		if (cl_thirdperson.GetBool() == false) {
			g_ThirdPersonManager.SetDesiredCameraOffset(new(cam_idealdist.GetFloat(), cam_idealdistright.GetFloat(), cam_idealdistup.GetFloat()));
			g_ThirdPersonManager.SetOverridingThirdPerson(true);
		}

		SourceDllMain.input.CAM_ToThirdPerson();

		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		localPlayer?.ThirdPersonSwitch(true);
	}

	[ConCommand("firstperson", "Switch to firstperson camera.")]
	static void firstperson() {
		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		// if (localPlayer && !localPlayer.CanUseFirstPersonCommand())
		// 	return;

		if (cl_thirdperson.GetBool() == false)
			g_ThirdPersonManager.SetOverridingThirdPerson(false);

		SourceDllMain.input.CAM_ToFirstPerson();

		localPlayer?.ThirdPersonSwitch(false);
	}

	public void CAM_ToThirdPerson() {
		engine.GetViewAngles(out QAngle viewangles);

		if (!CameraInThirdPerson) {
			CameraInThirdPerson = true;
			g_ThirdPersonManager.SetCameraOffsetAngles(new(viewangles[YAW], viewangles[PITCH], CAM_MIN_DIST));
		}

		cam_command.SetValue(0);
	}

	public void CAM_ToFirstPerson() {
		g_ThirdPersonManager.SetDesiredCameraOffset(vec3_origin);

		CameraInThirdPerson = false;
		cam_command.SetValue(0);
	}

	public bool CAM_IsThirdPerson() => CameraInThirdPerson;

	public bool CAM_IsOrthographic() => CameraIsOrthographic;

	public void CAM_OrthographicSize(out float w, out float h) {
		w = c_orthowidth.GetFloat();
		h = c_orthoheight.GetFloat();
	}

	static KeyButtonState cam_pitchup, cam_pitchdown, cam_yawleft, cam_yawright;
	static KeyButtonState cam_in, cam_out;

	public void CAM_Think() {
		if (CameraThirdData != null) {
			CAM_CameraThirdThink();
			return;
		}

		Vector3 idealAngles = new();
		float sensitivity;

		switch (cam_command.GetInt()) {
			case (int)CamCommand.ToThirdPerson:
				CAM_ToThirdPerson();
				break;
			case (int)CamCommand.ToFirstPerson:
				CAM_ToFirstPerson();
				break;
			default:
				break;
		}

		g_ThirdPersonManager.Update();

		if (!CameraInThirdPerson)
			return;

		idealAngles[PITCH] = cam_idealpitch.GetFloat();
		idealAngles[YAW] = cam_idealyaw.GetFloat();
		idealAngles[DIST] = cam_idealdist.GetFloat();

		if (CameraMovingWithMouse) {
			GetMousePos(out int cpx, out int cpy);

			CameraX = cpx;
			CameraY = cpy;

			if (!CameraDistanceMove) {
				GetWindowCenter(out int x, out int y);

				if (CameraX > x) {
					if (idealAngles[YAW] < c_maxyaw.GetFloat())
						idealAngles[YAW] += CAM_ANGLE_MOVE * ((CameraX - x) / 2);
					if (idealAngles[YAW] > c_maxyaw.GetFloat())
						idealAngles[YAW] = c_maxyaw.GetFloat();
				}
				else if (CameraX < x) {
					if (idealAngles[YAW] > c_minyaw.GetFloat())
						idealAngles[YAW] -= CAM_ANGLE_MOVE * ((x - CameraX) / 2);
					if (idealAngles[YAW] < c_minyaw.GetFloat())
						idealAngles[YAW] = c_minyaw.GetFloat();
				}

				if (CameraY > y) {
					if (idealAngles[PITCH] < c_maxpitch.GetFloat())
						idealAngles[PITCH] += CAM_ANGLE_MOVE * ((CameraY - y) / 2);
					if (idealAngles[PITCH] > c_maxpitch.GetFloat())
						idealAngles[PITCH] = c_maxpitch.GetFloat();
				}
				else if (CameraY < y) {
					if (idealAngles[PITCH] > c_minpitch.GetFloat())
						idealAngles[PITCH] -= CAM_ANGLE_MOVE * ((y - CameraY) / 2);
					if (idealAngles[PITCH] < c_minpitch.GetFloat())
						idealAngles[PITCH] = c_minpitch.GetFloat();
				}

				if ((sensitivity = gHUD.GetSensitivity()) != 0) {
					CameraOldX = (int)(CameraX * sensitivity);
					CameraOldY = (int)(CameraY * sensitivity);
				}
				else {
					CameraOldX = CameraX;
					CameraOldY = CameraY;
				}
				ResetMouse();
			}
		}

		if (KeyState(ref cam_pitchup) != 0)
			idealAngles[PITCH] += cam_idealdelta.GetFloat();
		else if (KeyState(ref cam_pitchdown) != 0)
			idealAngles[PITCH] -= cam_idealdelta.GetFloat();

		if (KeyState(ref cam_yawleft) != 0)
			idealAngles[YAW] -= cam_idealdelta.GetFloat();
		else if (KeyState(ref cam_yawright) != 0)
			idealAngles[YAW] += cam_idealdelta.GetFloat();

		if (KeyState(ref cam_in) != 0) {
			idealAngles[DIST] -= 2 * cam_idealdelta.GetFloat();
			if (idealAngles[DIST] < CAM_MIN_DIST) {
				idealAngles[PITCH] = 0;
				idealAngles[YAW] = 0;
				idealAngles[DIST] = CAM_MIN_DIST;
			}
		}
		else if (KeyState(ref cam_out) != 0)
			idealAngles[DIST] += 2 * cam_idealdelta.GetFloat();

		if (CameraDistanceMove) {
			GetWindowCenter(out int x, out int y);

			if (CameraY > y) {
				if (idealAngles[DIST] < c_maxdistance.GetFloat()) {
					idealAngles[DIST] += cam_idealdelta.GetFloat() * ((CameraY - y) / 2);
				}
				if (idealAngles[DIST] > c_maxdistance.GetFloat()) {
					idealAngles[DIST] = c_maxdistance.GetFloat();
				}
			}
			else if (CameraY < y) {
				if (idealAngles[DIST] > c_mindistance.GetFloat()) {
					idealAngles[DIST] -= cam_idealdelta.GetFloat() * ((y - CameraY) / 2);
				}
				if (idealAngles[DIST] < c_mindistance.GetFloat()) {
					idealAngles[DIST] = c_mindistance.GetFloat();
				}
			}
			CameraOldX = CameraX * gHUD.GetSensitivity();
			CameraOldY = CameraY * gHUD.GetSensitivity();
			ResetMouse();
		}

		engine.GetViewAngles(out QAngle viewangles);
		// s_oldAngles = viewangles;

		if (idealAngles[PITCH] > 180)
			idealAngles[PITCH] -= 360;
		else if (idealAngles[PITCH] < -180)
			idealAngles[PITCH] += 360;

		if (idealAngles[YAW] >= 180)
			idealAngles[YAW] -= 360;
		else if (idealAngles[YAW] <= -180)
			idealAngles[YAW] += 360;

		idealAngles[PITCH] = Math.Clamp(idealAngles[PITCH], c_minpitch.GetFloat(), c_maxpitch.GetFloat());
		idealAngles[YAW] = Math.Clamp(idealAngles[YAW], c_minyaw.GetFloat(), c_maxyaw.GetFloat());
		idealAngles[DIST] = Math.Clamp(idealAngles[DIST], c_mindistance.GetFloat(), c_maxdistance.GetFloat());

		cam_idealpitch.SetValue(idealAngles[PITCH]);
		cam_idealyaw.SetValue(idealAngles[YAW]);
		cam_idealdist.SetValue(idealAngles[DIST]);
		MathLib.VectorCopy(g_ThirdPersonManager.GetCameraOffsetAngles(), out Vector3 camOffset);

		if (cam_snapto.GetBool()) {
			camOffset[YAW] = cam_idealyaw.GetFloat() + viewangles[YAW];
			camOffset[PITCH] = cam_idealpitch.GetFloat() + viewangles[PITCH];
			camOffset[DIST] = cam_idealdist.GetFloat();
		}
		else {
			float lag = Math.Max(1, 1 + cam_ideallag.GetFloat());

			if (camOffset[YAW] - viewangles[YAW] != cam_idealyaw.GetFloat())
				camOffset[YAW] = MoveToward(camOffset[YAW], cam_idealyaw.GetFloat() + viewangles[YAW], lag);

			if (camOffset[PITCH] - viewangles[PITCH] != cam_idealpitch.GetFloat())
				camOffset[PITCH] = MoveToward(camOffset[PITCH], cam_idealpitch.GetFloat() + viewangles[PITCH], lag);

			if (Math.Abs(camOffset[DIST] - cam_idealdist.GetFloat()) < 2.0)
				camOffset[DIST] = cam_idealdist.GetFloat();
			else
				camOffset[DIST] += (cam_idealdist.GetFloat() - camOffset[DIST]) / lag;
		}

		if (cam_collision.GetBool()) {
			QAngle desiredCamAngles = new(camOffset[PITCH], camOffset[YAW], camOffset[DIST]);

			if (g_ThirdPersonManager.IsOverridingThirdPerson() == false)
				desiredCamAngles = viewangles;

			g_ThirdPersonManager.PositionCamera(C_BasePlayer.GetLocalPlayer(), desiredCamAngles);
		}

		if (cam_showangles.GetBool()) {
			// engine.Con_NPrintf(4, "Pitch: %6.1f   Yaw: %6.1f %38s", viewangles[PITCH], viewangles[YAW], "view angles");
			// engine.Con_NPrintf(6, "Pitch: %6.1f   Yaw: %6.1f   Dist: %6.1f %19s", cam_idealpitch.GetFloat(), cam_idealyaw.GetFloat(), cam_idealdist.GetFloat(), "ideal angles");
			// engine.Con_NPrintf(8, "Pitch: %6.1f   Yaw: %6.1f   Dist: %6.1f %16s", g_ThirdPersonManager.GetCameraOffsetAngles()[PITCH], g_ThirdPersonManager.GetCameraOffsetAngles()[YAW], g_ThirdPersonManager.GetCameraOffsetAngles()[DIST], "camera offset");
		}

		g_ThirdPersonManager.SetCameraOffsetAngles(camOffset);
	}

	static float MoveToward(float cur, float goal, float lag) {
		if (cur != goal) {
			if (Math.Abs(cur - goal) > 180.0) {
				if (cur < goal)
					cur += 360.0f;
				else
					cur -= 360.0f;
			}

			if (cur < goal) {
				if (cur < goal - 1.0)
					cur += (goal - cur) / lag;
				else
					cur = goal;
			}
			else {
				if (cur > goal + 1.0)
					cur -= (cur - goal) / lag;
				else
					cur = goal;
			}
		}

		if (cur < 0)
			cur += 360.0f;
		else if (cur >= 360)
			cur -= 360;

		return cur;
	}


	void CAM_CameraThirdThink() {

	}

	CameraThirdData? CameraThirdData;
	public void CAM_SetCameraThirdData(CameraThirdData? data, QAngle offset) {
		CameraThirdData = data;

		Vector3 tempOffset = new();
		tempOffset[PITCH] = offset[PITCH];
		tempOffset[YAW] = offset[YAW];
		tempOffset[DIST] = offset[DIST];

		g_ThirdPersonManager.SetCameraOffsetAngles(tempOffset);
	}
}

public struct CameraThirdData
{
	public float Pitch;
	public float Yaw;
	public float Dist;
	public float Lag;
	public Vector3 HullMin;
	public Vector3 HullMax;
}