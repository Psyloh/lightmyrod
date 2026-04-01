using System;
using Vintagestory.API.MathTools;

namespace LightMyRod
{
	public enum Coverage
	{
		Partial, Full
	}

	public class Pattern
	{
		readonly int _sideLength;
		readonly int[] _data;
		readonly int _centerIndex;

		public Pattern(int maxOffset)
		{
			_sideLength = (maxOffset << 1) + 1;

			var size = _sideLength * _sideLength;
			_data = new int[size];

			_centerIndex = size >> 1;
		}

		public ref int this[int x, int z] => ref _data[_centerIndex + z * _sideLength + x];
	}

	public class ProtectiveArea(LightningRod rod, ModConfig config)
	{
		public LightningRod Rod => rod;
		public BlockPos RodPosition => rod.Position;

		int? _maxYDiff;
		public int MaxYDiff
		{
			get
			{
				_maxYDiff ??= config.GetMaxYDiff();
				return _maxYDiff.Value;
			}
		}

		static Pattern? _pattern;
		public Pattern Pattern
		{
			get
			{
				_pattern ??= CalcPattern();
				return _pattern;
			}
		}

		void LoopUntil(Func<float, bool> predicate, ref int yDiff, ref float radius)
		{
			while (yDiff > 0 && predicate(radius))
			{
				yDiff--;
				radius = config.GetRadiusFor(yDiff);
			}
		}

		Pattern CalcPattern()
		{
			var maxOffset = config.GetMaxOffset();
			var pattern = new Pattern(maxOffset);
			pattern[0, 0] = -1;

			var origin = new FastVec2f(0, 0);

			for (var z = -maxOffset; z < maxOffset; z++)
			{
				for (var x = -maxOffset; x < maxOffset; x++)
				{
					ref var value = ref pattern[x, z];

					if (value < 0) continue;

					var inner = new FastVec2f(
						x < 0 ? -x - 1 : x,
						z < 0 ? -z - 1 : z
					);

					if (origin.DistanceTo(inner) >= config.MaxRadius)
					{
						value = -1;
						continue;
					}

					var outer = inner + 1;

					var yDiff = MaxYDiff;
					float radius = config.MaxRadius;

					LoopUntil(r => origin.DistanceTo(outer) <= r, ref yDiff, ref radius);
					value = yDiff << 10;

					LoopUntil(r => origin.DistanceTo(inner) <= r, ref yDiff, ref radius);
					value |= yDiff;
				}
			}

			return pattern;
		}

		BlockPos GetTopPosition() => rod.Position.UpCopy((int)Math.Ceiling(config.ArtificialElevation));

		public void Feed(Registry registry)
		{
			var topPos = GetTopPosition();
			var maxOffset = config.GetMaxOffset();
			var minHeight = topPos.Y - MaxYDiff;

			for (var z = -maxOffset; z < maxOffset; z++)
			{
				for (var x = -maxOffset; x < maxOffset; x++)
				{
					var value = Pattern[x, z];

					if (value < 0) continue;

					var above = topPos.Y - (value & 0x3ff);
					var limit = topPos.Y - (value >> 10);

					var verticalPos = new BlockPos(topPos.X + x, 0, topPos.Z + z);
					verticalPos.Y = ApiHelper.GetRainMapHeight(verticalPos);

					if (limit > minHeight)
					{
						registry.RegisterUntil(bp => bp.Y < limit, ref verticalPos, Coverage.Full);
					}

					registry.RegisterUntil(bp => bp.Y < above, ref verticalPos, Coverage.Partial);
				}
			}
		}
	}
}