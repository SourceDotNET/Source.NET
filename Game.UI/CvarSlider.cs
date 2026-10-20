using Source;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

[PanelAlias("CCvarSlider")]
public class CvarSlider : Slider
{
	public static Panel Create_CvarSlider() => new CvarSlider(null, null);

	[PanelAnimationVar("use_convar_minmax", "0", "bool")] protected bool UseCvarMinMax;
	bool AllowOutOfRange;
	bool ModifiedOnce;
	float StartValue;
	int iStartValue;
	int LastSliderValue;
	float CurrentValue;
	string? CvarName;
	bool CreatedInCode;
	float MinValue;
	float MaxValue;
	public CvarSlider(Panel? panel, ReadOnlySpan<char> name) : base(panel, name) {
		SetupSlider(0, 1, "", false);
		CreatedInCode = false;
		AddActionSignalTarget(this);
	}

	public CvarSlider(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text, float minValue, float maxValue, ReadOnlySpan<char> cvarName, bool allowOutOfRange = false) : base(parent, name) {
		AddActionSignalTarget(this);
		SetupSlider(minValue, maxValue, cvarName, allowOutOfRange);
		CreatedInCode = true;
	}

	public void SetupSlider(float minValue, float maxValue, ReadOnlySpan<char> cvarName, bool allowOutOfRange) {
		ConVarRef var = new(cvarName, true);

		if (var.IsValid()) {
			float cvarMin;
			if (var.GetMin(out double CVarMin)) {
				cvarMin = (float)CVarMin;
				minValue = UseCvarMinMax ? cvarMin : Math.Max(minValue, cvarMin);
			}

			float cvarMax;
			if (var.GetMax(out double CVarMax)) {
				cvarMax = (float)CVarMax;
				maxValue = UseCvarMinMax ? cvarMax : Math.Min(maxValue, cvarMax);
			}
		}

		MinValue = minValue;
		MaxValue = maxValue;

		SetRange((int)(CVARSLIDER_SCALE_FACTOR * MinValue), (int)(CVARSLIDER_SCALE_FACTOR * MaxValue));

		Span<char> min = stackalloc char[32];
		Span<char> max = stackalloc char[32];

		minValue.TryFormat(min, out int minLen, "F2");
		maxValue.TryFormat(max, out int maxLen, "F2");

		SetTickCaptions(min, max);

		CvarName = cvarName.ToString();

		ModifiedOnce = false;
		AllowOutOfRange = allowOutOfRange;

		Reset();
	}

	public void SetTickColor(Color color) => TickColor = color;

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		if (!CreatedInCode) {
			float minValue = resourceData.GetFloat("minvalue", 0);
			float maxValue = resourceData.GetFloat("maxvalue", 1);
			ReadOnlySpan<char> cvarName = resourceData.GetString("cvar_name", "");
			bool allowOutOfRange = resourceData.GetInt("allowoutofrange", 0) != 0;
			SetupSlider(minValue, maxValue, cvarName, allowOutOfRange);

			// HACK: If our parent is a property page, we want the dialog containing it
			if (GetParent() is PropertyPage && GetParent()!.GetParent() != null)
				GetParent()!.GetParent()!.AddActionSignalTarget(this);
			else
				GetParent()!.AddActionSignalTarget(this);
		}
	}

	public override void GetSettings(KeyValues outResourceData) {
		base.GetSettings(outResourceData);

		if (!CreatedInCode) {
			outResourceData.SetFloat("minvalue", MinValue);
			outResourceData.SetFloat("maxvalue", MaxValue);
			outResourceData.SetString("cvar_name", CvarName!);
			outResourceData.SetInt("allowoutofrange", AllowOutOfRange ? 1 : 0);
		}
	}

	public void SetCVarName(ReadOnlySpan<char> cvarName) {
		CvarName = cvarName.ToString();
		ModifiedOnce = false;
		Reset();
	}

	public void SetMinMaxValues(float minValue, float maxValue, bool setTickDisplay) {
		SetRange((int)(CVARSLIDER_SCALE_FACTOR * minValue), (int)(CVARSLIDER_SCALE_FACTOR * maxValue));

		if (setTickDisplay) {
			Span<char> min = stackalloc char[32];
			Span<char> max = stackalloc char[32];

			minValue.TryFormat(min, out int minLen, "F2");
			maxValue.TryFormat(max, out int maxLen, "F2");

			SetTickCaptions(min, max);
		}

		Reset();
	}

	public override void Paint() {
		ConVarRef var = new(CvarName, true);
		if (!var.IsValid()) {
			base.Paint();
			return;
		}

		float curValue = var.GetFloat();
		if (curValue != StartValue) {
			int value = (int)(CVARSLIDER_SCALE_FACTOR * curValue);
			StartValue = curValue;
			CurrentValue = curValue;

			SetValue(value);
			iStartValue = GetValue();
		}
		base.Paint();
	}

	public void ApplyChanges() {
		if (ModifiedOnce) {
			iStartValue = GetValue();
			if (AllowOutOfRange)
				StartValue = CurrentValue;
			else
				StartValue = iStartValue / CVARSLIDER_SCALE_FACTOR;

			ConVarRef var = new(CvarName!, true);
			if (!var.IsValid())
				return;
			var.SetValue(StartValue);
		}
	}

	public float GetSliderValue() {
		if (AllowOutOfRange)
			return CurrentValue;
		else
			return GetValue() / CVARSLIDER_SCALE_FACTOR;
	}

	public const float CVARSLIDER_SCALE_FACTOR = 100.0f;

	public void SetSliderValue(float value) {
		int val = (int)(CVARSLIDER_SCALE_FACTOR * value);
		SetValue(val, false);

		LastSliderValue = GetValue();

		if (CurrentValue != value) {
			CurrentValue = value;
			ModifiedOnce = true;
		}
	}

	public void Reset() {
		ConVarRef var = new(CvarName!, true);

		if (!var.IsValid()) {
			CurrentValue = StartValue = 0.0f;
			SetValue(0, false);
			iStartValue = GetValue();
			LastSliderValue = iStartValue;
			return;
		}

		StartValue = var.GetFloat();
		CurrentValue = StartValue;

		int value = (int)(CVARSLIDER_SCALE_FACTOR * StartValue);
		SetValue(value, false);

		iStartValue = GetValue();
		LastSliderValue = iStartValue;
	}

	public bool HasBeenModified() {
		if (GetValue() != iStartValue)
			ModifiedOnce = true;
		return ModifiedOnce;
	}

	readonly static KeyValues KV_ControlModified = new("ControlModified");
	[UiMessage("SliderMoved")]
	public void OnSliderMoved() {
		if (HasBeenModified()) {
			if (LastSliderValue != GetValue()) {
				LastSliderValue = GetValue();
				CurrentValue = LastSliderValue / CVARSLIDER_SCALE_FACTOR;
			}

			PostActionSignal(KV_ControlModified);
		}
	}

	[UiMessage("SliderDragEnd")]
	public void OnSliderDragEnd() {
		if (!CreatedInCode)
			ApplyChanges();
	}
}
