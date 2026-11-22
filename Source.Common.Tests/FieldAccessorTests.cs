using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common.Tests;

[InlineArray(4)]
public struct TestFieldsInt4
{
	int first;
}

[InlineArray(8)]
public struct TestFieldsChar8
{
	char first;
}

[InlineArray(4)]
public struct TestFieldsString4
{
	string? first;
}

public struct TestFieldsInner
{
	public int Value;
	public float Scale;
}

public struct TestFieldsWrapped
{
	public int Raw;

	public static implicit operator int(TestFieldsWrapped wrapped) => wrapped.Raw;
	public static implicit operator TestFieldsWrapped(int raw) => new() { Raw = raw };
}

public partial class TestFieldsBase
{
	public int Value;
	public int Other;
	public TestFieldsInt4 Values;
	[NetworkName("m_flRenamed")]
	public float Renamed;
	public TestFieldsInner Inner;
	public Vector3 Position;
	public QAngle Angles;
	[NetworkArraySize(3)]
	public NetworkArray<int> Counts = new(3);
	public List<int> Items = [];
	public TestFieldsChar8 Text;
	public bool Flag;
	public double Precise;
	public readonly int ReadOnlyValue = 4;
	int hidden = 2;
	public TestFieldsWrapped Wrapped;
	public string? Label;
	public TestFieldsString4 Names;
	public char Letter;
	public byte Tiny;
	public nint Native;

	public int Hidden => hidden;

	[NetworkVar] public partial int Tracked { get; set; }
	[NetworkVar] public partial TestFieldsInt4 TrackedValues { get; set; }
	[NetworkVar] public partial Vector3 TrackedPosition { get; set; }

	public readonly List<IFieldAccessor> Changes = [];
	void NetworkStateChanged(IFieldAccessor field) => Changes.Add(field);
}

public class TestFieldsDerived : TestFieldsBase
{
	public int DerivedValue;
}

public sealed class TestKeylessAccessor() : FieldAccessor<int>(typeof(TestFieldsBase), nameof(TestFieldsBase.Value), nameof(TestFieldsBase.Value))
{
	public override ref int Ref(object instance) => ref ((TestFieldsBase)instance).Value;
}

public class FieldAccessorTests
{
	[Fact]
	public void ScalarGetAndSet() {
		TestFieldsBase obj = new() { Value = 5 };
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.Equal(5, field.GetValue<int>(obj));
		Assert.True(field.SetValue(obj, 9));
		Assert.Equal(9, obj.Value);
	}

	[Fact]
	public void ScalarConvertsBetweenNumericTypes() {
		TestFieldsBase obj = new() { Value = 7, Renamed = 2.5f, Precise = 1.25 };

		Assert.Equal(7.0f, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)).GetValue<float>(obj));
		Assert.Equal(7L, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)).GetValue<long>(obj));
		Assert.Equal(2.5, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Renamed)).GetValue<double>(obj));
		Assert.Equal(1.25f, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Precise)).GetValue<float>(obj));

		FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)).SetValue(obj, 2.9f);
		Assert.Equal(2, obj.Value);

		FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Renamed)).SetValue(obj, 3);
		Assert.Equal(3.0f, obj.Renamed);
	}

	[Fact]
	public void BoolConvertsToAndFromIntegers() {
		TestFieldsBase obj = new();
		IFieldAccessor flag = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Flag));

		flag.SetValue(obj, 1);
		Assert.True(obj.Flag);
		Assert.Equal(1, flag.GetValue<int>(obj));

		flag.SetValue(obj, 0);
		Assert.False(obj.Flag);
		Assert.Equal(0, flag.GetValue<int>(obj));
	}

	[Fact]
	public void ScalarMetadata() {
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.Equal(nameof(TestFieldsBase.Value), field.Name);
		Assert.Equal(nameof(TestFieldsBase.Value), field.NetworkName);
		Assert.Equal(typeof(TestFieldsBase), ((IFieldAccessor)field).DeclaringType);
		Assert.Equal(typeof(int), ((IFieldAccessor)field).FieldType);
		Assert.Equal(1, field.Length);
		Assert.Equal(-1, field.Index);
	}

	[Fact]
	public void DeclaringTypeIsTheAccessorOwner() {
		IFieldAccessor field = FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Value));

		Assert.Equal(typeof(TestFieldsDerived), field.DeclaringType);
	}

	[Fact]
	public void NetworkNameComesFromAttribute() {
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Renamed));

		Assert.Equal(nameof(TestFieldsBase.Renamed), field.Name);
		Assert.Equal("m_flRenamed", field.NetworkName);
	}

	[Fact]
	public void NamedAccessorUsesGivenName() {
		DynamicAccessor field = FIELD<TestFieldsBase>.OF_NAMED(nameof(TestFieldsBase.Value), "m_iCustom");
		TestFieldsBase obj = new() { Value = 3 };

		Assert.Equal("m_iCustom", field.Name);
		Assert.Equal("m_iCustom", field.NetworkName);
		Assert.Equal(3, field.GetValue<int>(obj));
	}

	[Fact]
	public void DerivedAccessorReadsBaseField() {
		TestFieldsDerived obj = new() { Value = 11, DerivedValue = 12 };

		Assert.Equal(11, FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Value)).GetValue<int>(obj));
		Assert.Equal(12, FIELD<TestFieldsDerived>.OF(nameof(TestFieldsDerived.DerivedValue)).GetValue<int>(obj));
		Assert.Equal(11, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)).GetValue<int>(obj));
	}

	[Fact]
	public void NestedPathReadsAndWritesInPlace() {
		TestFieldsBase obj = new();
		obj.Inner.Value = 4;
		DynamicAccessor field = FIELD<TestFieldsBase>.OF("Inner.Value");

		Assert.Equal("Inner.Value", field.Name);
		Assert.Equal(4, field.GetValue<int>(obj));

		field.SetValue(obj, 8);
		Assert.Equal(8, obj.Inner.Value);
	}

	[Fact]
	public void ScalarCopyToAndCopyFrom() {
		TestFieldsBase obj = new() { Value = 21 };
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Span<int> target = stackalloc int[1];
		field.CopyTo(obj, target);
		Assert.Equal(21, target[0]);

		field.CopyTo(obj, Span<int>.Empty);

		Span<int> source = [33];
		field.CopyFrom(obj, source);
		Assert.Equal(33, obj.Value);
	}

	[Fact]
	public void TryGetSpanAliasesValueTypeField() {
		TestFieldsBase obj = new() { Value = 1 };
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.True(field.TryGetSpan(obj, 1, out Span<int> span));
		Assert.Equal(1, span.Length);
		span[0] = 6;
		Assert.Equal(6, obj.Value);
	}

	[Fact]
	public void TryGetSpanReinterpretsVector() {
		TestFieldsBase obj = new() { Position = new(1, 2, 3) };
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Position));

		Assert.True(field.TryGetSpan(obj, 3, out Span<float> span));
		Assert.Equal([1f, 2f, 3f], span.ToArray());
	}

	[Fact]
	public void TryGetSpanRejectsSizeMismatchAndReferenceTypes() {
		TestFieldsBase obj = new();

		Assert.False(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)).TryGetSpan(obj, 1, out Span<long> _));
		Assert.False(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Items)).TryGetSpan(obj, 1, out Span<int> _));
	}

	[Fact]
	public void TryGetSpanAliasesStringField() {
		TestFieldsBase obj = new() { Label = "a" };
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Label));

		Assert.True(field.TryGetSpan(obj, 1, out Span<string?> span));
		Assert.Equal("a", span[0]);
		span[0] = "b";
		Assert.Equal("b", obj.Label);
		Assert.False(field.TryGetSpan(obj, 2, out Span<string?> _));
	}

	[Fact]
	public void TryGetSpanAliasesInlineStringArray() {
		TestFieldsBase obj = new();
		obj.Names[2] = "c";
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Names));

		Assert.True(field.TryGetSpan(obj, 4, out Span<string?> span));
		Assert.Equal(4, span.Length);
		Assert.Equal("c", span[2]);
		span[0] = "a";
		Assert.Equal("a", obj.Names[0]);
		Assert.True(field.TryGetSpan(obj, 1, out Span<string?> first));
		Assert.Equal("a", first[0]);
		Assert.False(field.TryGetSpan(obj, 5, out Span<string?> _));
	}

	[Fact]
	public void InlineArrayElementsGetAndSet() {
		TestFieldsBase obj = new();
		obj.Values[2] = 5;
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));

		Assert.Equal(4, array.Length);
		Assert.Equal(typeof(int), array.Info.ContainedType);
		Assert.False(array.Info.IsList);
		Assert.Equal(5, array.AtIndex(2)!.GetValue<int>(obj));
		Assert.Equal(5, array.GetElement<int>(obj, 2));

		array.AtIndex(1)!.SetValue(obj, 7);
		array.SetElement(obj, 3, 9);
		Assert.Equal(7, obj.Values[1]);
		Assert.Equal(9, obj.Values[3]);
	}

	[Fact]
	public void ArrayAtIndexIsBoundedAndCached() {
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));

		Assert.Null(array.AtIndex(-1));
		Assert.Null(array.AtIndex(4));
		Assert.Same(array.AtIndex(2), array.AtIndex(2));
	}

	[Fact]
	public void ArrayElementMetadata() {
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));
		DynamicAccessor element = array.AtIndex(2)!;

		Assert.Equal(2, element.Index);
		Assert.Equal("Values[2]", element.Name);
		Assert.Equal("Values[2]", element.NetworkName);
		Assert.Equal("Values[2]", array.ElementNetworkName(2));
		Assert.Equal(typeof(int), ((IFieldAccessor)element).FieldType);
	}

	[Fact]
	public void ArrayIndexAccessorMatchesAtIndex() {
		TestFieldsBase obj = new();
		obj.Values[3] = 13;

		DynamicArrayIndexAccessor element = FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), 3);

		Assert.Equal(13, element.GetValue<int>(obj));
		Assert.False(element.IsAVectorElement);
	}

	[Fact]
	public void SendInfoArrayUsesArrayNetworkName() {
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));
		DynamicArrayIndexAccessor element = FIELD<TestFieldsBase>.OF_SENDINFO_ARRAY(nameof(TestFieldsBase.Values));

		Assert.Equal(0, element.Index);
		Assert.Equal(array.NetworkName, element.NetworkName);
	}

	[Fact]
	public void ArrayAccessorCopiesAllElements() {
		TestFieldsBase obj = new();
		IFieldAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));

		array.CopyFrom(obj, [1, 2, 3, 4]);
		Assert.Equal([1, 2, 3, 4], ((ReadOnlySpan<int>)obj.Values).ToArray());

		Span<int> target = stackalloc int[4];
		array.CopyTo(obj, target);
		Assert.Equal([1, 2, 3, 4], target.ToArray());
	}

	[Fact]
	public void InlineArrayScalarCopyFromClearsTail() {
		TestFieldsBase obj = new();
		obj.Values[2] = 50;
		obj.Values[3] = 60;
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Values));

		field.CopyFrom(obj, [8, 9]);
		Assert.Equal([8, 9, 0, 0], ((ReadOnlySpan<int>)obj.Values).ToArray());

		Span<int> target = stackalloc int[4];
		field.CopyTo(obj, target);
		Assert.Equal([8, 9, 0, 0], target.ToArray());
	}

	[Fact]
	public void CharInlineArrayConvertsToString() {
		TestFieldsBase obj = new();
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Text));

		field.CopyFrom(obj, "abc".ToCharArray().AsSpan());
		Assert.Equal("abc", field.GetValue<string>(obj));
	}

	[Fact]
	public void VectorElementsGetAndSet() {
		TestFieldsBase obj = new() { Position = new(1, 2, 3) };
		DynamicArrayIndexAccessor y = FIELD<TestFieldsBase>.OF_VECTORELEM(nameof(TestFieldsBase.Position), 1);

		Assert.True(y.IsAVectorElement);
		Assert.Equal(2f, y.GetValue<float>(obj));

		y.SetValue(obj, 7f);
		Assert.Equal(new Vector3(1, 7, 3), obj.Position);
		Assert.Equal(3, FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Position)).Length);
	}

	[Fact]
	public void AngleElementsGetAndSet() {
		TestFieldsBase obj = new() { Angles = new(10, 20, 30) };
		DynamicArrayAccessor angles = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Angles));

		Assert.Equal(3, angles.Length);
		Assert.Equal(20f, angles.GetElement<float>(obj, 1));

		angles.SetElement(obj, 2, 45f);
		Assert.Equal(45f, obj.Angles.Z);
	}

	[Fact]
	public void NetworkArrayElementsGetAndSet() {
		TestFieldsBase obj = new();
		obj.Counts.Set(1, 4);
		DynamicArrayAccessor counts = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Counts));

		Assert.Equal(3, counts.Length);
		Assert.Equal(4, counts.AtIndex(1)!.GetValue<int>(obj));

		counts.AtIndex(2)!.SetValue(obj, 6);
		Assert.Equal(6, obj.Counts[2]);
	}

	[Fact]
	public void NetworkArrayScalarSpanCoversElements() {
		TestFieldsBase obj = new();
		obj.Counts.Set(0, 1);
		obj.Counts.Set(2, 3);
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Counts));

		Assert.True(field.TryGetSpan(obj, 3, out Span<int> span));
		Assert.Equal([1, 0, 3], span.ToArray());
	}

	[Fact]
	public void ListElementsGetAndSet() {
		TestFieldsBase obj = new() { Items = [1, 2, 3] };
		DynamicArrayAccessor items = FIELD<TestFieldsBase>.OF_LIST(nameof(TestFieldsBase.Items), 8);

		Assert.Equal(8, items.Length);
		Assert.True(items.Info.IsList);
		Assert.Equal(2, items.AtIndex(1)!.GetValue<int>(obj));

		items.AtIndex(2)!.SetValue(obj, 30);
		Assert.Equal(30, obj.Items[2]);
	}

	[Fact]
	public void NetworkVarSetterRecordsChanges() {
		TestFieldsBase obj = new();

		obj.Tracked = 5;
		obj.Tracked = 5;
		Assert.Single(obj.Changes);
		Assert.Same(TestFieldsBase.NetworkVarFields.Tracked, obj.Changes[0]);

		obj.TrackedForModify() = 6;
		Assert.Equal(2, obj.Changes.Count);
		Assert.Equal(6, obj.Tracked);
	}

	[Fact]
	public void NetworkVarAccessorReadsBackingField() {
		TestFieldsBase obj = new() { Tracked = 12 };
		IFieldAccessor field = TestFieldsBase.NetworkVarFields.Tracked;

		Assert.Equal(12, field.GetValue<int>(obj));
		field.SetValue(obj, 14);
		Assert.Equal(14, obj.Tracked);
		Assert.Equal(nameof(TestFieldsBase.Tracked), field.Name);
	}

	[Fact]
	public void FieldOfNetworkVarPropertyMatchesGeneratedAccessor() {
		TestFieldsBase obj = new() { Tracked = 3 };
		IFieldAccessor viaField = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Tracked));
		IFieldAccessor viaDerived = FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Tracked));

		Assert.Equal(3, viaField.GetValue<int>(obj));
		Assert.Equal(TestFieldsBase.NetworkVarFields.Tracked, viaField);
		Assert.Equal(TestFieldsBase.NetworkVarFields.Tracked, viaDerived);
		Assert.Equal(TestFieldsBase.NetworkVarFields.Tracked.GetHashCode(), viaField.GetHashCode());
	}

	[Fact]
	public void SeparateAccessorsToSameFieldAreEqual() {
		IFieldAccessor a = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));
		IFieldAccessor b = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.NotSame(a, b);
		Assert.Equal(a, b);
		Assert.Equal(a.GetHashCode(), b.GetHashCode());
	}

	[Fact]
	public void AccessorThroughDerivedTypeEqualsBaseAccessor() {
		IFieldAccessor baseAccessor = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));
		IFieldAccessor derivedAccessor = FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Value));

		Assert.Equal(baseAccessor, derivedAccessor);
		Assert.Equal(baseAccessor.GetHashCode(), derivedAccessor.GetHashCode());
	}

	[Fact]
	public void NamedAccessorEqualsUnnamedAccessorToSameField() {
		IFieldAccessor named = FIELD<TestFieldsBase>.OF_NAMED(nameof(TestFieldsBase.Value), "m_iCustom");
		IFieldAccessor plain = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.Equal(plain, named);
	}

	[Fact]
	public void NestedPathAccessorsMatchByPath() {
		IFieldAccessor value = FIELD<TestFieldsBase>.OF("Inner.Value");
		IFieldAccessor derivedValue = FIELD<TestFieldsDerived>.OF("Inner.Value");
		IFieldAccessor scale = FIELD<TestFieldsBase>.OF("Inner.Scale");

		Assert.Equal(value, derivedValue);
		Assert.NotEqual(value, scale);
	}

	[Fact]
	public void DifferentFieldsAreNotEqual() {
		IFieldAccessor value = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));
		IFieldAccessor other = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Other));
		IFieldAccessor derivedValue = FIELD<TestFieldsDerived>.OF(nameof(TestFieldsDerived.DerivedValue));

		Assert.NotEqual(value, other);
		Assert.NotEqual(value, derivedValue);
	}

	[Fact]
	public void ArrayElementsMatchByIndex() {
		IFieldAccessor baseElement = FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), 2);
		IFieldAccessor derivedElement = FIELD<TestFieldsDerived>.OF_ARRAY(nameof(TestFieldsBase.Values)).AtIndex(2)!;
		IFieldAccessor otherElement = FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), 3);

		Assert.Equal(baseElement, derivedElement);
		Assert.Equal(baseElement.GetHashCode(), derivedElement.GetHashCode());
		Assert.NotEqual(baseElement, otherElement);
	}

	[Fact]
	public void ArrayElementIsNotEqualToWholeArray() {
		IFieldAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));
		IFieldAccessor element = FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), 0);

		Assert.NotEqual(array, element);
	}

	[Fact]
	public void DictionaryLookupFindsFieldThroughAnotherAccessor() {
		Dictionary<IFieldAccessor, ushort> map = new() {
			[FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Value))] = 7,
			[FIELD<TestFieldsDerived>.OF_ARRAY(nameof(TestFieldsBase.Values)).AtIndex(1)!] = 9,
		};

		Assert.True(map.TryGetValue(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)), out ushort scalarIndex));
		Assert.Equal(7, scalarIndex);

		Assert.True(map.TryGetValue(FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), 1), out ushort elementIndex));
		Assert.Equal(9, elementIndex);

		Assert.False(map.ContainsKey(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Other))));
	}

	[Fact]
	public void ReadOnlyFieldIsReadAndWritten() {
		TestFieldsBase obj = new();
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.ReadOnlyValue));

		Assert.Equal(4, field.GetValue<int>(obj));
		field.SetValue(obj, 9);
		Assert.Equal(9, obj.ReadOnlyValue);
	}

	[Fact]
	public void PrivateFieldOnOwnerIsAccessible() {
		TestFieldsBase obj = new();
		IFieldAccessor field = FIELD<TestFieldsBase>.OF("hidden");

		Assert.Equal(2, field.GetValue<int>(obj));
		field.SetValue(obj, 5);
		Assert.Equal(5, obj.Hidden);
	}

	[Fact]
	public void PrivateBaseFieldIsNotAvailableThroughDerivedOwner() {
		Assert.Throws<KeyNotFoundException>(() => FIELD<TestFieldsDerived>.OF("hidden"));
	}

	[Fact]
	public void MissingFieldThrows() {
		Assert.Throws<KeyNotFoundException>(() => FIELD<TestFieldsBase>.OF("DoesNotExist"));
		Assert.Throws<KeyNotFoundException>(() => FIELD<TestFieldsBase>.OF_ARRAY("DoesNotExist"));
	}

	[Fact]
	public void StructOwnerWritesThroughBox() {
		object boxed = new TestFieldsInner { Value = 1 };
		IFieldAccessor field = FIELD<TestFieldsInner>.OF(nameof(TestFieldsInner.Value));

		Assert.Equal(1, field.GetValue<int>(boxed));
		field.SetValue(boxed, 3);
		Assert.Equal(3, ((TestFieldsInner)boxed).Value);
	}

	[Fact]
	public void ConstantIndexExpressionAddressesElement() {
		TestFieldsBase obj = new();
		obj.Values[2] = 17;
		DynamicAccessor field = FIELD<TestFieldsBase>.OF("Values[2]");

		Assert.Equal("Values[2]", field.Name);
		Assert.Equal(17, field.GetValue<int>(obj));

		field.SetValue(obj, 18);
		Assert.Equal(18, obj.Values[2]);
	}

	[Fact]
	public void NegativeArrayIndexUsesAbsoluteValue() {
		TestFieldsBase obj = new();
		obj.Values[2] = 23;
		DynamicArrayIndexAccessor element = FIELD<TestFieldsBase>.OF_ARRAYINDEX(nameof(TestFieldsBase.Values), -2);

		Assert.Equal(2, element.Index);
		Assert.Equal(23, element.GetValue<int>(obj));
	}

	[Fact]
	public void ImplicitOperatorsAreUsedForConversion() {
		TestFieldsBase obj = new() { Wrapped = new() { Raw = 8 } };
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Wrapped));

		Assert.Equal(8, field.GetValue<int>(obj));
		field.SetValue(obj, 12);
		Assert.Equal(12, obj.Wrapped.Raw);
	}

	[Fact]
	public void ReferenceTypesAreCast() {
		TestFieldsBase obj = new() { Label = "crowbar" };
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Label));

		Assert.Equal("crowbar", field.GetValue<object>(obj));
		Assert.Equal("crowbar", field.GetValue<string>(obj));

		field.SetValue<object>(obj, "pistol");
		Assert.Equal("pistol", obj.Label);
	}

	[Fact]
	public void UnsupportedConversionThrows() {
		TestFieldsBase obj = new();
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.Throws<NotSupportedException>(() => field.GetValue<Vector3>(obj));
	}

	[Fact]
	public void NarrowAndCharConversions() {
		TestFieldsBase obj = new();

		FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Tiny)).SetValue(obj, 300);
		Assert.Equal(44, obj.Tiny);

		FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Letter)).SetValue(obj, 65);
		Assert.Equal('A', obj.Letter);
		Assert.Equal(65, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Letter)).GetValue<int>(obj));

		FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Native)).SetValue(obj, -5L);
		Assert.Equal((nint)(-5), obj.Native);
		Assert.Equal(-5, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Native)).GetValue<int>(obj));
	}

	[Fact]
	public void ListElementAccessorReadsAndWrites() {
		List<int> list = [10, 20, 30];
		ListElementAccessor<int> element = new(1);

		Assert.Equal("[1]", element.Name);
		Assert.Equal(typeof(List<int>), element.DeclaringType);
		Assert.Equal(typeof(int), element.FieldType);
		Assert.Equal(1, element.Length);
		Assert.Equal(1, element.Index);
		Assert.Equal(20, element.GetValue<int>(list));

		element.SetValue(list, 25);
		Assert.Equal(25, list[1]);

		Span<int> target = stackalloc int[1];
		element.CopyTo(list, target);
		Assert.Equal(25, target[0]);

		element.CopyFrom(list, [27]);
		Assert.Equal(27, list[1]);
	}

	[Fact]
	public void DataMapFieldUsesDeclaringClassAccessor() {
		TestFieldsDerived obj = new() { Value = 31 };
		TypeDescription desc = DEFINE<TestFieldsDerived>.FIELD(nameof(TestFieldsBase.Value), FieldType.Integer);
		DynamicAccessor accessor = desc.Accessor;

		Assert.Equal(typeof(TestFieldsBase), accessor.TargetType);
		Assert.Equal(31, accessor.GetValue<int>(obj));
		Assert.Equal(FIELD<TestFieldsDerived>.OF(nameof(TestFieldsBase.Value)), accessor);
		Assert.Same(accessor, desc.Accessor);
	}

	[Fact]
	public void DataMapKeyFieldKeepsMapName() {
		TypeDescription desc = DEFINE<TestFieldsBase>.KEYFIELD(nameof(TestFieldsBase.Value), FieldType.Integer, "value");

		Assert.Equal("value", desc.ExternalName);
		Assert.Equal(nameof(TestFieldsBase.Value), desc.FieldName);
		Assert.True((desc.Flags & FieldTypeDescFlags.Key) != 0);
		Assert.True((desc.Flags & FieldTypeDescFlags.Save) != 0);
	}

	[Fact]
	public void NetworkVarInlineArrayProperty() {
		TestFieldsBase obj = new();
		TestFieldsInt4 values = default;
		values[1] = 3;

		obj.TrackedValues = values;
		obj.TrackedValues = values;
		Assert.Single(obj.Changes);
		Assert.Same(TestFieldsBase.NetworkVarFields.TrackedValues, obj.Changes[0]);

		values[3] = 1;
		obj.TrackedValues = values;
		Assert.Equal(2, obj.Changes.Count);
		obj.Changes.Clear();
		values[3] = 0;
		obj.TrackedValues = values;
		Assert.Single(obj.Changes);

		Span<int> target = stackalloc int[4];
		TestFieldsBase.NetworkVarFields.TrackedValues.CopyTo(obj, target);
		Assert.Equal([0, 3, 0, 0], target.ToArray());

		TestFieldsBase.NetworkVarFields.TrackedValues.CopyFrom(obj, [7]);
		TestFieldsInt4 copied = obj.TrackedValues;
		Assert.Equal([7, 0, 0, 0], ((ReadOnlySpan<int>)copied).ToArray());
	}

	[Fact]
	public void NetworkVarVectorProperty() {
		TestFieldsBase obj = new();

		obj.TrackedPosition = new(1, 2, 3);
		obj.TrackedPosition = new(1, 2, 3);
		Assert.Single(obj.Changes);
		Assert.Equal(new Vector3(1, 2, 3), TestFieldsBase.NetworkVarFields.TrackedPosition.GetValue<Vector3>(obj));
		Assert.Equal(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.TrackedPosition)), TestFieldsBase.NetworkVarFields.TrackedPosition);
	}

	[Fact]
	public void ArrayAtIndexThroughInterface() {
		TestFieldsBase obj = new();
		obj.Values[1] = 8;
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));

		IFieldAccessor element = ((IFieldAccessorIndexable)array).AtIndex(1);

		Assert.Same(array.AtIndex(1), element);
		Assert.Equal(8, element.GetValue<int>(obj));
	}

	[Fact]
	public void ScalarAtIndexReturnsItself() {
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.Same(field, field.AtIndex(3));
		Assert.Same(field, ((IFieldAccessorIndexable)field).AtIndex(3));
	}

	[Fact]
	public void AccessorEqualsItself() {
		DynamicAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value));

		Assert.True(field.Equals(field));
		Assert.False(field.Equals(null));
		Assert.False(field.Equals(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Other))));
	}

	[Fact]
	public void KeylessAccessorsCompareByReference() {
		TestKeylessAccessor first = new(), second = new();

		Assert.Null(first.FieldKey);
		Assert.Equal(0, first.FieldKeyId);
		Assert.Equal(first, first);
		Assert.NotEqual(first, second);
		Assert.NotEqual<DynamicAccessor>(first, FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)));
		Assert.NotEqual<DynamicAccessor>(FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Value)), first);
		Assert.Equal(RuntimeHelpers.GetHashCode(first), first.GetHashCode());

		TestFieldsBase obj = new() { Value = 12 };
		Assert.Equal(12, first.GetValue<int>(obj));
	}

	[Fact]
	public void ArrayElementHasNoSpan() {
		TestFieldsBase obj = new();
		DynamicAccessor element = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values)).AtIndex(0)!;

		Assert.False(element.TryGetSpan<int>(obj, 1, out Span<int> span));
		Assert.True(span.IsEmpty);
	}

	[Fact]
	public void ListElementEmptySpans() {
		List<int> list = [10, 20];
		ListElementAccessor<int> element = new(0);

		element.CopyTo(list, Span<int>.Empty);
		element.CopyFrom(list, Span<int>.Empty);

		Assert.Equal(0, list[0]);
	}

	[Fact]
	public void InlineArrayCopiesWholeValue() {
		TestFieldsBase obj = new();
		IFieldAccessor field = FIELD<TestFieldsBase>.OF(nameof(TestFieldsBase.Values));
		TestFieldsInt4 values = default;
		values[0] = 1;
		values[3] = 4;

		field.CopyFrom(obj, [values]);
		Assert.Equal([1, 0, 0, 4], ((ReadOnlySpan<int>)obj.Values).ToArray());

		Span<TestFieldsInt4> target = new TestFieldsInt4[1];
		field.CopyTo(obj, target);
		Assert.Equal([1, 0, 0, 4], ((ReadOnlySpan<int>)target[0]).ToArray());
	}

	[Fact]
	public void ArrayAccessorWholeValue() {
		TestFieldsBase obj = new();
		DynamicArrayAccessor array = FIELD<TestFieldsBase>.OF_ARRAY(nameof(TestFieldsBase.Values));
		TestFieldsInt4 values = default;
		values[2] = 9;

		Assert.True(array.SetValue(obj, values));
		Assert.Equal(9, obj.Values[2]);

		TestFieldsInt4 read = array.GetValue<TestFieldsInt4>(obj);
		Assert.Equal([0, 0, 9, 0], ((ReadOnlySpan<int>)read).ToArray());
	}

	[Fact]
	public void SignedSourceConversions() {
		Assert.Equal(-3, FieldConvert<sbyte, int>.Convert((sbyte)-3));
		Assert.Equal(-300, FieldConvert<short, int>.Convert((short)-300));
		Assert.Equal(-7L, FieldConvert<int, long>.Convert(-7));
		Assert.Equal(-9, FieldConvert<long, int>.Convert(-9L));
	}

	[Fact]
	public void UnsignedSourceConversions() {
		Assert.Equal(5L, FieldConvert<uint, long>.Convert(5u));
		Assert.Equal(6, FieldConvert<ulong, int>.Convert(6ul));
		Assert.Equal(7, FieldConvert<nuint, int>.Convert((nuint)7));
		Assert.Equal(2f, FieldConvert<uint, float>.Convert(2u));
		Assert.Equal(3d, FieldConvert<ushort, double>.Convert((ushort)3));
		Assert.True(FieldConvert<byte, bool>.Convert((byte)1));
		Assert.False(FieldConvert<byte, bool>.Convert((byte)0));
	}

	[Fact]
	public void FloatSourceConversions() {
		Assert.True(FieldConvert<float, bool>.Convert(0.5f));
		Assert.False(FieldConvert<double, bool>.Convert(0d));
		Assert.Equal((sbyte)-4, FieldConvert<double, sbyte>.Convert(-4.7));
		Assert.Equal((short)-500, FieldConvert<float, short>.Convert(-500.2f));
		Assert.Equal(-6, FieldConvert<double, int>.Convert(-6.9));
		Assert.Equal(1L << 40, FieldConvert<double, long>.Convert((double)(1L << 40)));
		Assert.Equal((byte)200, FieldConvert<double, byte>.Convert(200.9));
		Assert.Equal((ushort)60000, FieldConvert<float, ushort>.Convert(60000f));
		Assert.Equal(4000000000u, FieldConvert<double, uint>.Convert(4000000000d));
		Assert.Equal(1ul << 50, FieldConvert<double, ulong>.Convert((double)(1ul << 50)));
		Assert.Equal(1.5f, FieldConvert<double, float>.Convert(1.5));
		Assert.Equal(2.5, FieldConvert<float, double>.Convert(2.5f));
	}

	[Fact]
	public void NumericKindClassification() {
		Assert.Equal(NumericKind.Signed, NumericConvert.Classify(typeof(nint)));
		Assert.Equal(NumericKind.Unsigned, NumericConvert.Classify(typeof(nuint)));
		Assert.Equal(NumericKind.Unsigned, NumericConvert.Classify(typeof(char)));
		Assert.Equal(NumericKind.Bool, NumericConvert.Classify(typeof(bool)));
		Assert.Equal(NumericKind.Float, NumericConvert.Classify(typeof(float)));
		Assert.Equal(NumericKind.None, NumericConvert.Classify(typeof(decimal)));
		Assert.Equal(NumericKind.None, NumericConvert.Classify(typeof(string)));
	}

	[Fact]
	public void NetworkArraySizeAttributeKeepsSize() {
		Assert.Equal(5, new NetworkArraySizeAttribute(5).Size);
	}

	[Fact]
	public void NetworkArrayMembers() {
		NetworkArray<int> array = new(3);
		int[] raw = array;

		Assert.Same(array.Value, raw);
		Assert.Same(array.Value, array.Base);
		Assert.Equal(3, array.Count());
	}

	[Fact]
	public void NetworkArrayHookSeesChanges() {
		NetworkArray<int> array = new(3);
		List<int> changed = [];
		array.Hook((ref int value) => changed.Add(value));

		array.Set(1, 5);
		array.Set(1, 5);
		Assert.Single(changed);
		Assert.Equal(5, array[1]);

		array.GetForModify(2) = 8;
		Assert.Equal(2, changed.Count);
		Assert.Equal(8, array.Get(2));
	}

	[Fact]
	public void NetworkArrayWithoutHook() {
		NetworkArray<int> array = new(2);

		array.Set(0, 3);
		array.GetForModify(1) = 4;

		Assert.Equal([3, 4], array.Value);
	}
}
