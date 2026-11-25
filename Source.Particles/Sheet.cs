using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Source.Particles;

public struct SequenceSampleTextureCoords
{
	public float LeftU0;
	public float TopV0;
	public float RightU0;
	public float BottomV0;

	public float LeftU1;
	public float TopV1;
	public float RightU1;
	public float BottomV1;
}

[InlineArray(Sheet.MAX_IMAGES_PER_FRAME_IN_MEMORY)]
public struct SequenceSampleTextureCoordsArray
{
	SequenceSampleTextureCoords Element;
}

public struct SheetSequenceSample
{
	public SequenceSampleTextureCoordsArray TextureCoordData;
	public float BlendFactor;
}

public sealed class Sheet
{
	public const int MAX_SEQUENCES = 64;
	public const int SEQUENCE_SAMPLE_COUNT = 1024;
	public const int MAX_IMAGES_PER_FRAME_ON_DISK = 4;
	public const int MAX_IMAGES_PER_FRAME_IN_MEMORY = 2;

	public readonly SheetSequenceSample[]?[] Samples = new SheetSequenceSample[]?[MAX_SEQUENCES];
	public readonly bool[] Clamp = new bool[MAX_SEQUENCES];
	public readonly bool[] SequenceIsCopyOfAnotherSequence = new bool[MAX_SEQUENCES];
	public readonly int[] NumFrames = new int[MAX_SEQUENCES];
	public readonly float[] FrameSpan = new float[MAX_SEQUENCES];

	ref struct Reader(ReadOnlySpan<byte> data)
	{
		readonly ReadOnlySpan<byte> Data = data;
		int Position;

		public int GetInt() {
			if (Position + 4 > Data.Length)
				return 0;
			int value = BinaryPrimitives.ReadInt32LittleEndian(Data[Position..]);
			Position += 4;
			return value;
		}

		public float GetFloat() {
			if (Position + 4 > Data.Length)
				return 0;
			float value = BinaryPrimitives.ReadSingleLittleEndian(Data[Position..]);
			Position += 4;
			return value;
		}
	}

	public Sheet(ReadOnlySpan<byte> data) {
		Reader buf = new(data);

		int version = buf.GetInt();
		int numCoordsPerFrame = version != 0 ? MAX_IMAGES_PER_FRAME_ON_DISK : 1;

		int numSequences = buf.GetInt();

		float[] interpKnot = new float[SEQUENCE_SAMPLE_COUNT];
		float[] interpValue = new float[SEQUENCE_SAMPLE_COUNT];
		SheetSequenceSample[] samples = new SheetSequenceSample[SEQUENCE_SAMPLE_COUNT];

		while (numSequences-- != 0) {
			int sequenceNumber = buf.GetInt();
			if ((uint)sequenceNumber >= MAX_SEQUENCES) {
				Warning($"sequence number {sequenceNumber} too high in sheet file!!!\n");
				return;
			}

			Clamp[sequenceNumber] = buf.GetInt() != 0;
			int frameCount = buf.GetInt();
			NumFrames[sequenceNumber] = frameCount;

			int timeSamples = frameCount == 1 ? 1 : SEQUENCE_SAMPLE_COUNT;
			SheetSequenceSample[] outSamples = new SheetSequenceSample[timeSamples];
			Samples[sequenceNumber] = outSamples;

			int totalSequenceTime = (int)buf.GetFloat();
			float curTime = 0.0f;
			for (int frame = 0; frame < frameCount && frame < SEQUENCE_SAMPLE_COUNT; frame++) {
				float thisDuration = buf.GetFloat();
				interpValue[frame] = frame;
				interpKnot[frame] = SEQUENCE_SAMPLE_COUNT / (float)totalSequenceTime * curTime;

				ref SheetSequenceSample seq = ref samples[frame];
				seq.BlendFactor = 0.0f;
				for (int image = 0; image < numCoordsPerFrame; image++) {
					float left = buf.GetFloat();
					float top = buf.GetFloat();
					float right = buf.GetFloat();
					float bottom = buf.GetFloat();
					if (image >= MAX_IMAGES_PER_FRAME_IN_MEMORY)
						continue;

					ref SequenceSampleTextureCoords s = ref seq.TextureCoordData[image];
					s.LeftU0 = left;
					s.TopV0 = top;
					s.RightU0 = right;
					s.BottomV0 = bottom;
				}

				if (numCoordsPerFrame == 1)
					seq.TextureCoordData[1] = seq.TextureCoordData[0];

				curTime += thisDuration;
				FrameSpan[sequenceNumber] = curTime;
			}

			if (frameCount <= 0)
				continue;

			int knots = Math.Min(frameCount, SEQUENCE_SAMPLE_COUNT);
			for (int idx = 0; idx < timeSamples; idx++) {
				GetInterpolationData(interpKnot, interpValue, knots, SEQUENCE_SAMPLE_COUNT, idx, !Clamp[sequenceNumber], out float idxA, out float idxB, out float interp);
				ref SheetSequenceSample sA = ref samples[(int)idxA];
				ref SheetSequenceSample sB = ref samples[(int)idxB];
				ref SheetSequenceSample oseq = ref outSamples[idx];

				oseq.BlendFactor = interp;
				for (int image = 0; image < MAX_IMAGES_PER_FRAME_IN_MEMORY; image++) {
					ref SequenceSampleTextureCoords src0 = ref sA.TextureCoordData[image];
					ref SequenceSampleTextureCoords src1 = ref sB.TextureCoordData[image];
					ref SequenceSampleTextureCoords o = ref oseq.TextureCoordData[image];
					o.LeftU0 = src0.LeftU0;
					o.TopV0 = src0.TopV0;
					o.RightU0 = src0.RightU0;
					o.BottomV0 = src0.BottomV0;
					o.LeftU1 = src1.LeftU0;
					o.TopV1 = src1.TopV0;
					o.RightU1 = src1.RightU0;
					o.BottomV1 = src1.BottomV0;
				}
			}
		}

		for (int first = 0; first < MAX_SEQUENCES; first++) {
			if (Samples[first] == null)
				continue;

			for (int i = 0; i < MAX_SEQUENCES; i++) {
				if (Samples[i] != null)
					continue;

				Samples[i] = Samples[first];
				Clamp[i] = Clamp[first];
				NumFrames[i] = NumFrames[first];
				SequenceIsCopyOfAnotherSequence[i] = true;
			}
			break;
		}
	}

	static void GetInterpolationData(ReadOnlySpan<float> knotPositions, ReadOnlySpan<float> knotValues, int numValuesInList, int interpolationRange, float positionToInterpolateAt, bool wrap, out float valueA, out float valueB, out float interpolationValue) {
		int idx;
		for (idx = 0; idx < numValuesInList; idx++) {
			if (knotPositions[idx] >= positionToInterpolateAt)
				break;
		}

		int knot1, knot2;
		float offsetFromStartOfGap, sizeOfGap;
		if (idx == 0) {
			if (wrap) {
				knot1 = numValuesInList - 1;
				knot2 = 0;
				sizeOfGap = knotPositions[knot2] + (interpolationRange - knotPositions[knot1]);
				offsetFromStartOfGap = positionToInterpolateAt + (interpolationRange - knotPositions[knot1]);
			}
			else {
				valueA = valueB = knotValues[0];
				interpolationValue = 1.0f;
				return;
			}
		}
		else if (idx == numValuesInList) {
			if (wrap) {
				knot1 = numValuesInList - 1;
				knot2 = 0;
				sizeOfGap = knotPositions[knot2] + (interpolationRange - knotPositions[knot1]);
				offsetFromStartOfGap = positionToInterpolateAt - knotPositions[knot1];
			}
			else {
				valueA = valueB = knotValues[numValuesInList - 1];
				interpolationValue = 1.0f;
				return;
			}
		}
		else {
			knot1 = idx - 1;
			knot2 = idx;
			sizeOfGap = knotPositions[knot2] - knotPositions[knot1];
			offsetFromStartOfGap = positionToInterpolateAt - knotPositions[knot1];
		}

		valueA = knotValues[knot1];
		valueB = knotValues[knot2];
		interpolationValue = offsetFromStartOfGap / sizeOfGap;
	}

	public static ref readonly SheetSequenceSample GetSampleForSequence(Sheet? sheet, float creationTime, float curTime, float ageScale, int sequence) {
		if (sheet == null)
			return ref Unsafe.NullRef<SheetSequenceSample>();

		SheetSequenceSample[]? samples = sheet.Samples[sequence];
		if (samples == null)
			return ref Unsafe.NullRef<SheetSequenceSample>();
		if (sheet.NumFrames[sequence] == 1)
			return ref samples[0];

		uint frame = (uint)(long)((curTime - creationTime) * ageScale);
		uint index = frame & 0x3FF;
		if (sheet.Clamp[sequence])
			index = frame < 0x400 ? frame : 0x3FF;
		return ref samples[index];
	}
}
