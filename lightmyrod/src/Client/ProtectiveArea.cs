using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	public enum Coverage
	{
		Partial, Full
	}
	//TODO: implement CenteredOnRod!
	public class Pattern
	{
		readonly int _sideLength;
		public int SideLength => _sideLength;
		readonly int[] _data;

		public Pattern(int maxOffset)
		{
			_sideLength = maxOffset + 1;
			_data = new int[_sideLength * _sideLength];
		}

		public ref int this[int x, int z] => ref _data[z * _sideLength + x];
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

		BlockPos? _topPosition;
		BlockPos TopPosition
		{
			get
			{
				_topPosition ??= rod.Position.UpCopy((int)Math.Ceiling(config.ArtificialElevation));
				return _topPosition;
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
			var pattern = new Pattern(config.GetMaxOffset());
			var origin = new FastVec2f(0, 0);

			for (var z = 0; z < pattern.SideLength; z++)
			{
				for (var x = 0; x < pattern.SideLength; x++)
				{
					ref var value = ref pattern[x, z];

					var inner = new FastVec2f(x, z);

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

		void FeedGranular(Registry registry, FeedContext context)
		{
			var above = TopPosition.Y - (context.Value & 0x3ff);
			var limitYDiff = context.Value >> 10;

			var verticalPos = new BlockPos(TopPosition.X + context.Coords.X, 0, TopPosition.Z + context.Coords.Z);
			verticalPos.Y = ApiHelper.GetRainMapHeight(verticalPos);

			if (limitYDiff < MaxYDiff)
			{
				registry.RegisterUntil(bp => bp.Y < TopPosition.Y - limitYDiff, ref verticalPos, Coverage.Full);
			}
			registry.RegisterUntil(bp => bp.Y < above, ref verticalPos, Coverage.Partial);
		}

		public async Task FeedParallel(Registry registry)
		{
			Parallel.ForEach(GetContexts(), new() { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, context =>
			{
				FeedGranular(registry, context);
			});
		}

		ConcurrentBag<FeedContext> GetContexts()
		{
			ConcurrentBag<FeedContext> contexts = [];
			Parallel.For(0, Pattern.SideLength, new() { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, z =>
			{
				for (var x = 0; x < Pattern.SideLength; x++)
				{
					var value = Pattern[x, z];
					if (value < 0)
					{
						break;
					}

					if (x > 0 || z > 0)
					{
						contexts.Add(new(value, new FastVec2i(x, z)));
					}
					contexts.Add(new(value, new FastVec2i(-x - 1, -z - 1)));
					contexts.Add(new(value, new FastVec2i(x, -z - 1)));
					contexts.Add(new(value, new FastVec2i(-x - 1, z)));
				}
			});
			return contexts;
		}
	}

	struct FeedContext(int value, FastVec2i coords)
	{
		public readonly int Value => value;
		public readonly FastVec2i Coords => coords;
	}
}