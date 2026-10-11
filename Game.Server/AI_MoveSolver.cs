global using static Game.Server.AI_MoveSolverGlobals;

using Game.Shared;

using System.Runtime.InteropServices;

namespace Game.Server;

public enum AI_MoveSuggType
{
	Move,

	AvoidDanger,
	AvoidObject,
	AvoidNPC,
	AvoidWorld,

	NoKnowledge,
	OscillationDeterrance,

	Invalid
}

[Flags]
public enum AI_MoveSuggestionFlags
{
	FavorLeft = 0x01,
	FavorRight = 0x02
}

public static class AI_MoveSolverGlobals
{
	public static float NormalizeAngle(float angle) {
		if (angle < 0.0f)
			angle += 360.0f;
		else if (angle >= 360.0f)
			angle -= 360.0f;
		return angle;
	}
}

public struct AI_Arc
{
	public void Set(float newCenter, float newSpan) {
		Center = NormalizeAngle(newCenter);
		Span = NormalizeAngle(newSpan);
	}

	public void SetByLimits(float yawRight, float yawLeft) {
		Span = yawLeft - yawRight;

		if (Span < 0)
			Span += 360;

		Center = yawRight + Span * 0.5f;

		if (Center >= 360)
			Center -= 360;
	}

	public float Center;
	public float Span;
}

public struct AI_MoveSuggestion
{
	public AI_MoveSuggestion() {
		Type = AI_MoveSuggType.Invalid;
		Weight = 0;
		Flags = 0;
	}

	public AI_MoveSuggestion(AI_MoveSuggType newType, float newWeight, float newDir, float newSpan, BaseEntity? entity = null) => Set(newType, newWeight, newDir, newSpan, entity);

	public AI_MoveSuggestion(AI_MoveSuggType newType, float newWeight, in AI_Arc arc, BaseEntity? entity = null) => Set(newType, newWeight, arc.Center, arc.Span, entity);

	public void Set(AI_MoveSuggType newType, float newWeight, float newDir, float newSpan, BaseEntity? entity = null) {
		Type = newType;
		Weight = newWeight;
		ObstacleEntity.Set(entity);
		Flags = 0;

		Arc.Set(newDir, newSpan);
	}

	public void Set(AI_MoveSuggType newType, float newWeight, in AI_Arc arc, BaseEntity? entity = null) => Set(newType, newWeight, arc.Center, arc.Span, entity);

	public AI_MoveSuggType Type;
	public float Weight;
	public AI_Arc Arc;
	public EHANDLE ObstacleEntity = new();
	public AI_MoveSuggestionFlags Flags;
}

public struct AI_MoveSolution
{
	public float Dir;
}

public class AI_MoveSolver
{
	const int REGS_RESERVE = 8;
	const float AIMS_EPS = 0.01f;

	const int NUM_SOLUTIONS = 120;
	const int SOLUTION_ANG = 360 / NUM_SOLUTIONS;

	readonly record struct AI_MoveSuggWeights(float Min, float Max);

	static readonly AI_MoveSuggWeights[] g_AI_MoveSuggWeights = [
		new(0.20f, 1.00f),
		new(-0.00f, -0.25f),
		new(-0.00f, -0.25f),
		new(-0.00f, -0.25f),
		new(-0.00f, -0.25f),
		new(-1.00f, -1.00f),
		new(-0.60f, -0.60f),
		new(0.00f, 0.00f),
	];

	struct Solution
	{
		public float Bias;
		public float HighBias;
		public int HighSuggestion;
	}

	static float V_round(float f) => (int)(f + 0.5f);

	public void AddRegulation(in AI_MoveSuggestion suggestion) => Regulations.Add(suggestion);

	public void AddRegulations(ReadOnlySpan<AI_MoveSuggestion> suggestions) {
		for (int i = 0; i < suggestions.Length; ++i)
			Regulations.Add(suggestions[i]);
	}

	public bool HaveRegulations() => Regulations.Count > 0;
	public void ClearRegulations() => Regulations.Clear();

	public bool Solve(in AI_MoveSuggestion suggestion, out AI_MoveSolution result) => Solve(new ReadOnlySpan<AI_MoveSuggestion>(in suggestion), out result);

	public bool Solve(ReadOnlySpan<AI_MoveSuggestion> suggestionsIn, out AI_MoveSolution result) {
		result = default;

		if (suggestionsIn.Length == 0)
			return false;

		if (suggestionsIn.Length == 1 && Regulations.Count == 0 && suggestionsIn[0].Type == AI_MoveSuggType.Move) {
			result.Dir = suggestionsIn[0].Arc.Center;
			return true;
		}

		int count = Regulations.Count + suggestionsIn.Length;
		Span<AI_MoveSuggestion> suggestions = count <= 32 ? stackalloc AI_MoveSuggestion[count] : new AI_MoveSuggestion[count];
		suggestionsIn.CopyTo(suggestions);
		CollectionsMarshal.AsSpan(Regulations).CopyTo(suggestions[suggestionsIn.Length..]);

		Span<Solution> solutions = stackalloc Solution[NUM_SOLUTIONS];
		for (int i = 0; i < NUM_SOLUTIONS; ++i)
			solutions[i].HighSuggestion = -1;

		Assert(suggestions.Length >= 1);
		NormalizeSuggestions(suggestions);

		for (int suggestion = 0; suggestion < suggestions.Length; ++suggestion) {
			ref AI_MoveSuggestion current = ref suggestions[suggestion];

			float halfSpan = current.Arc.Span * 0.5f;
			int center = (int)V_round((halfSpan * NUM_SOLUTIONS) / 360);
			int left = (int)((current.Arc.Span * NUM_SOLUTIONS) / 360);

			float angRight = current.Arc.Center - halfSpan;

			if (angRight < 0.0f)
				angRight += 360;

			int baseIndex = (int)((angRight * NUM_SOLUTIONS) / 360);

			const float positiveDegradePer180 = 0.05f;
			const float positiveDegrade = positiveDegradePer180 / (NUM_SOLUTIONS * 0.5f);

			for (int i = 0; i < left + 1; ++i) {
				float bias = 0.0f;

				if (current.Weight > 0) {
					int offset = center - i;
					float degrade = Math.Abs(offset) * positiveDegrade;

					if (((current.Flags & AI_MoveSuggestionFlags.FavorLeft) != 0 && i > center) ||
						((current.Flags & AI_MoveSuggestionFlags.FavorRight) != 0 && i < center))
						degrade *= 0.9f;

					bias = current.Weight - (current.Weight * degrade);
				}
				else
					bias = current.Weight;

				int curSolution = (baseIndex + i) % NUM_SOLUTIONS;

				solutions[curSolution].Bias += bias;
				if (bias > solutions[curSolution].HighBias) {
					solutions[curSolution].HighBias = bias;
					solutions[curSolution].HighSuggestion = suggestion;
				}
			}
		}

		int best = -1;
		float biasBest = 0;

		for (int i = 0; i < NUM_SOLUTIONS; ++i) {
			if (solutions[i].Bias > biasBest) {
				best = i;
				biasBest = solutions[i].Bias;
			}
		}

		if (best == -1)
			return false;

		float resultDir = best * SOLUTION_ANG;

		float suggestionCenter = suggestions[solutions[best].HighSuggestion].Arc.Center;

		if (suggestionCenter > resultDir && suggestionCenter <= resultDir + SOLUTION_ANG)
			resultDir = suggestionCenter;

		result.Dir = resultDir;

		return true;
	}

	static void NormalizeSuggestions(Span<AI_MoveSuggestion> suggestions) {
		for (int i = 0; i < suggestions.Length; i++) {
			ref AI_MoveSuggestion suggestion = ref suggestions[i];
			float min = g_AI_MoveSuggWeights[(int)suggestion.Type].Min;
			float max = g_AI_MoveSuggWeights[(int)suggestion.Type].Max;

			Assert(suggestion.Weight >= -AIMS_EPS && suggestion.Weight <= 1.0f + AIMS_EPS);

			if (suggestion.Weight < AIMS_EPS)
				suggestion.Weight = 0.0f;
			else
				suggestion.Weight = ((max - min) * suggestion.Weight) + min;

			while (suggestion.Arc.Center < 0)
				suggestion.Arc.Center += 360;

			while (suggestion.Arc.Center >= 360)
				suggestion.Arc.Center -= 360;
		}
	}

	public bool HaveRegulationForObstacle(BaseEntity? entity) {
		for (int i = 0; i < Regulations.Count; ++i) {
			if (Regulations[i].ObstacleEntity.Get() != null &&
				entity == Regulations[i].ObstacleEntity.Get())
				return true;
		}
		return false;
	}

	readonly List<AI_MoveSuggestion> Regulations = new(REGS_RESERVE);
}
