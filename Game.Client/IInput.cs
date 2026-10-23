using Game.Client;
using Game.Shared;

using Source.Common.Bitbuffers;
using Source.Common.Input;
using Source.Common.Mathematics;

namespace Source.Common.Client;

public interface IInput
{
	public void CreateMove(int sequenceNumber, double inputSampleFrametime, bool active);
	public bool WriteUsercmdDeltaToBuffer(bf_write buf, int from, int to, bool isNewCommand);
	public void EncodeUserCmdToBuffer(bf_write buf, int slot);
	public void DecodeUserCmdFromBuffer(bf_read buf, int slot);
	public void MakeWeaponSelection(BaseCombatWeapon? weapon);
	void Init();
	float KeyState(ref KeyButtonState key);
	int KeyEvent(int eventcode, ButtonCode keynum, ReadOnlySpan<char> currentBinding);
	void ExtraMouseSample(double frametime, bool active);
	void ActivateMouse();
	void DeactivateMouse();
#if GMOD_DLL
	void MouseWheeled(int delta);
#endif
	void ClearStates();
	void CAM_Think();
	ref UserCmd GetUserCmd(int sequenceNumber);
	InButtons GetButtonBits(int resetState);
	void ClearInputButton(InButtons bits);

	void CAM_ToThirdPerson();
	void CAM_ToFirstPerson();
	bool CAM_IsThirdPerson();
	void CAM_SetCameraThirdData(CameraThirdData? value, QAngle vec3_angle);
	void AccumulateMouse();
}

public enum CamCommand
{
	None = 0,
	ToThirdPerson,
	ToFirstPerson
}
