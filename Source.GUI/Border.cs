using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI;

public class Border : IBorder
{
	readonly public ISurface Surface = Singleton<ISurface>();

	public string? Name;

	protected readonly int[] Inset = new int[4];

	struct Line
	{
		public Color Color;
		public int StartOffset;
		public int EndOffset;
	}

	struct Side
	{
		public Line[] Lines;
		public readonly int Length => Lines?.Length ?? 0;
		public Side() {
			Lines = [];
		}
	}

	readonly Side[] BorderSides = new Side[4];
	PaintBackgroundType BackgroundType;

	public virtual void ParseSideSettings(Sides sideIndex, KeyValues? inResourceData, IScheme scheme) {
		if (inResourceData == null)
			return;

		int count = 0;
		KeyValues? kv;
		for (kv = inResourceData.GetFirstSubKey(); kv != null; kv = kv.GetNextKey()) 
			count++;

		BorderSides[(int)sideIndex].Lines = new Line[count];

		int index = 0;
		for (kv = inResourceData.GetFirstSubKey(); kv != null; kv = kv.GetNextKey()) {
			ref Line line = ref BorderSides[(int)sideIndex].Lines[index];

			ReadOnlySpan<char> col = kv.GetString("color", null);
			line.Color = scheme.GetColor(col, new(0, 0, 0, 0));

			col = kv.GetString("offset", null);
			int start = 0, end = 0;
			if (!col.IsEmpty) 
				new ScanF(col, "%d %d").Read(out start).Read(out end);
			
			line.StartOffset = start;
			line.EndOffset = end;

			index++;
		}
	}
	public virtual void ApplySchemeSettings(IScheme? scheme, KeyValues inResourceData) {
		if (scheme == null)
			return;

		ReadOnlySpan<char> insetString = inResourceData.GetString("inset", "0 0 0 0");

		GetInset(out int left, out int top, out int right, out int bottom);
		new ScanF(insetString, "%d %d %d %d").ReadOrKeep(ref left).ReadOrKeep(ref top).ReadOrKeep(ref right).ReadOrKeep(ref bottom);
		SetInset(left, top, right, bottom);

		// get the border information from the scheme
		ParseSideSettings(Sides.Left, inResourceData.FindKey("Left"), scheme);
		ParseSideSettings(Sides.Top, inResourceData.FindKey("Top"), scheme);
		ParseSideSettings(Sides.Right, inResourceData.FindKey("Right"), scheme);
		ParseSideSettings(Sides.Bottom, inResourceData.FindKey("Bottom"), scheme);

		BackgroundType = (PaintBackgroundType)inResourceData.GetInt("backgroundtype");
	}

	public virtual PaintBackgroundType GetBackgroundType() => BackgroundType;

	public virtual void GetInset(out int left, out int top, out int right, out int bottom) {
		left = Inset[(int)Sides.Left];
		top = Inset[(int)Sides.Top];
		right = Inset[(int)Sides.Right];
		bottom = Inset[(int)Sides.Bottom];
	}

	public virtual ReadOnlySpan<char> GetName() => Name;

	public virtual void Paint(IPanel panel) {
		panel.GetSize(out int wide, out int tall);
		Paint(0, 0, wide, tall, Sides.None, 0, 0);
	}

	public virtual void Paint(int x, int y, int wide, int tall) =>	Paint(x, y, wide, tall, Sides.None, 0, 0);

	public virtual void Paint(int x, int y, int wide, int tall, Sides breakSide, int breakStart, int breakStop) {
		int i;

		for (i = 0; i < BorderSides[(int)Sides.Left].Length; i++) {
			ref Line line = ref BorderSides[(int)Sides.Left].Lines[i];
			Surface.DrawSetColor(line.Color[0], line.Color[1], line.Color[2], line.Color[3]);

			if (breakSide == Sides.Left) {
				if (breakStart > 0) 
					Surface.DrawFilledRect(x + i, y + line.StartOffset, x + i + 1, y + breakStart);

				if (breakStop < (tall - line.EndOffset)) 
					Surface.DrawFilledRect(x + i, y + breakStop + 1, x + i + 1, tall - line.EndOffset);
			}
			else 
				Surface.DrawFilledRect(x + i, y + line.StartOffset, x + i + 1, tall - line.EndOffset);
		}

		for (i = 0; i < BorderSides[(int)Sides.Top].Length; i++) {
			ref Line line = ref BorderSides[(int)Sides.Top].Lines[i];
			Surface.DrawSetColor(line.Color[0], line.Color[1], line.Color[2], line.Color[3]);

			if (breakSide == Sides.Top) {
				if (breakStart > 0) 
					Surface.DrawFilledRect(x + line.StartOffset, y + i, x + breakStart, y + i + 1);

				if (breakStop < (wide - line.EndOffset)) 
					Surface.DrawFilledRect(x + breakStop + 1, y + i, wide - line.EndOffset, y + i + 1);
			}
			else 
				Surface.DrawFilledRect(x + line.StartOffset, y + i, wide - line.EndOffset, y + i + 1);
		}

		for (i = 0; i < BorderSides[(int)Sides.Right].Length; i++) {
			ref Line line = ref BorderSides[(int)Sides.Right].Lines[i];
			Surface.DrawSetColor(line.Color[0], line.Color[1], line.Color[2], line.Color[3]);
			Surface.DrawFilledRect(wide - (i + 1), y + line.StartOffset, (wide - (i + 1)) + 1, tall - line.EndOffset);
		}

		for (i = 0; i < BorderSides[(int)Sides.Bottom].Length; i++) {
			ref Line line = ref BorderSides[(int)Sides.Bottom].Lines[i];
			Surface.DrawSetColor(line.Color[0], line.Color[1], line.Color[2], line.Color[3]);
			Surface.DrawFilledRect(x + line.StartOffset, tall - (i + 1), wide - line.EndOffset, (tall - (i + 1)) + 1);
		}
	}

	public virtual bool PaintFirst() => false;

	public virtual void SetInset(int left, int top, int right, int bottom) {
		Inset[(int)Sides.Left] = left;
		Inset[(int)Sides.Top] = top;
		Inset[(int)Sides.Right] = right;
		Inset[(int)Sides.Bottom] = bottom;
	}

	public virtual void SetName(ReadOnlySpan<char> name) {
		Name = new(name);
	}
}
