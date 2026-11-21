using Datamodel;

using System.Globalization;
using System.Numerics;

using DmColor = Datamodel.Color;
using DmQAngle = Datamodel.QAngle;
using SourceColor = Source.Color;
using SourceQAngle = Source.Common.Mathematics.QAngle;

namespace Source.Particles;

public enum DmAttributeType
{
	Unknown = 0,
	Element,
	Int,
	Float,
	Bool,
	String,
	Void,
	ObjectId,
	Color,
	Vector2,
	Vector3,
	Vector4,
	QAngle,
	Quaternion,
	VMatrix
}

public readonly record struct DmxElementUnpackField(string AttributeName, string? DefaultString, DmAttributeType AttributeType, int Size);

public interface IDmxElementUnpackStructure
{
	IEnumerable<DmxElementUnpackField> Fields { get; }
	void UnpackIntoStructure(Element element, object target);
}

public static class DmxElementUnpack
{
	public static DmAttributeType GetAttributeType(object? value) => value switch {
		null => DmAttributeType.Element,
		Element => DmAttributeType.Element,
		int => DmAttributeType.Int,
		float => DmAttributeType.Float,
		bool => DmAttributeType.Bool,
		string => DmAttributeType.String,
		byte[] => DmAttributeType.Void,
		DmColor => DmAttributeType.Color,
		Vector2 => DmAttributeType.Vector2,
		Vector3 => DmAttributeType.Vector3,
		Vector4 => DmAttributeType.Vector4,
		DmQAngle => DmAttributeType.QAngle,
		Quaternion => DmAttributeType.Quaternion,
		Matrix4x4 => DmAttributeType.VMatrix,
		_ => DmAttributeType.Unknown
	};

	public static object? ParseValue(DmAttributeType type, string text) {
		string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		int I(int i) => i < tokens.Length ? ScanInt(tokens[i]) : 0;
		float F(int i) => i < tokens.Length ? ScanFloat(tokens[i]) : 0;
		return type switch {
			DmAttributeType.Int => I(0),
			DmAttributeType.Float => F(0),
			DmAttributeType.Bool => I(0) != 0,
			DmAttributeType.String => text,
			DmAttributeType.Color => new DmColor((byte)I(0), (byte)I(1), (byte)I(2), (byte)I(3)),
			DmAttributeType.Vector2 => new Vector2(F(0), F(1)),
			DmAttributeType.Vector3 => new Vector3(F(0), F(1), F(2)),
			DmAttributeType.Vector4 => new Vector4(F(0), F(1), F(2), F(3)),
			DmAttributeType.QAngle => new DmQAngle(F(0), F(1), F(2)),
			DmAttributeType.Quaternion => new Quaternion(F(0), F(1), F(2), F(3)),
			_ => null
		};
	}

	static int ScanInt(string token) {
		int end = 0;
		if (end < token.Length && (token[end] == '-' || token[end] == '+'))
			end++;
		while (end < token.Length && char.IsAsciiDigit(token[end]))
			end++;
		return int.TryParse(token.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : 0;
	}

	static float ScanFloat(string token) => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
}

public sealed class DmxElementUnpackStructure<T> : IDmxElementUnpackStructure where T : class
{
	readonly List<(DmxElementUnpackField Field, Action<T, object> Setter)> fields = [];

	public IEnumerable<DmxElementUnpackField> Fields => fields.Select(f => f.Field);

	DmxElementUnpackStructure<T> Add(string name, string? defaultString, DmAttributeType type, int size, Action<T, object> setter) {
		fields.Add((new DmxElementUnpackField(name, defaultString, type, size), setter));
		return this;
	}

	public DmxElementUnpackStructure<T> Int(string name, string? defaultString, Action<T, int> set)
		=> Add(name, defaultString, DmAttributeType.Int, sizeof(int), (t, v) => set(t, (int)v));
	public DmxElementUnpackStructure<T> Float(string name, string? defaultString, Action<T, float> set)
		=> Add(name, defaultString, DmAttributeType.Float, sizeof(float), (t, v) => set(t, (float)v));
	public DmxElementUnpackStructure<T> Bool(string name, string? defaultString, Action<T, bool> set)
		=> Add(name, defaultString, DmAttributeType.Bool, sizeof(bool), (t, v) => set(t, (bool)v));
	public DmxElementUnpackStructure<T> String(string name, string? defaultString, int size, Action<T, string> set)
		=> Add(name, defaultString, DmAttributeType.String, size, (t, v) => {
			string s = (string)v;
			set(t, s.Length >= size ? s[..(size - 1)] : s);
		});
	public DmxElementUnpackStructure<T> Color(string name, string? defaultString, Action<T, SourceColor> set)
		=> Add(name, defaultString, DmAttributeType.Color, 4, (t, v) => {
			DmColor c = (DmColor)v;
			set(t, new SourceColor(c.R, c.G, c.B, c.A));
		});
	public DmxElementUnpackStructure<T> Vector2(string name, string? defaultString, Action<T, Vector2> set)
		=> Add(name, defaultString, DmAttributeType.Vector2, 8, (t, v) => set(t, (Vector2)v));
	public DmxElementUnpackStructure<T> Vector3(string name, string? defaultString, Action<T, Vector3> set)
		=> Add(name, defaultString, DmAttributeType.Vector3, 12, (t, v) => set(t, (Vector3)v));
	public DmxElementUnpackStructure<T> Vector4(string name, string? defaultString, Action<T, Vector4> set)
		=> Add(name, defaultString, DmAttributeType.Vector4, 16, (t, v) => set(t, (Vector4)v));
	public DmxElementUnpackStructure<T> QAngle(string name, string? defaultString, Action<T, SourceQAngle> set)
		=> Add(name, defaultString, DmAttributeType.QAngle, 12, (t, v) => {
			DmQAngle a = (DmQAngle)v;
			set(t, new SourceQAngle(a.Pitch, a.Yaw, a.Roll));
		});

	public void UnpackIntoStructure(Element element, object target) => UnpackIntoStructure(element, (T)target);

	public void UnpackIntoStructure(Element element, T target) {
		foreach ((DmxElementUnpackField field, Action<T, object> setter) in fields) {
			if (field.AttributeType == DmAttributeType.Void)
				continue;

			if (!element.TryGetValue(field.AttributeName, out object? value)) {
				if (field.DefaultString == null)
					continue;
				value = DmxElementUnpack.ParseValue(field.AttributeType, field.DefaultString);
			}

			if (DmxElementUnpack.GetAttributeType(value) != field.AttributeType) {
				Warning($"CDmxElement::UnpackIntoStructure: Mismatched attribute type in attribute \"{field.AttributeName}\"!\n");
				continue;
			}

			setter(target, value!);
		}
	}
}
