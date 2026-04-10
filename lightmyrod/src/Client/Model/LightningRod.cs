using Vintagestory.API.MathTools;

namespace LightMyRod.Client.Model
{
	public struct LightningRod
	{
		readonly BlockPos _position;
		public readonly BlockPos Position => _position;
		public readonly Vec3i LocalPosition => ApiHelper.GetLocalPosition(_position);

		bool _active = false;
		public readonly bool Active => _active;

		public LightningRod(BlockPos position)
		{
			_position = position;
			Update();
		}

		public bool Update()
		{
			var current = _active;
			_active = ApiHelper.IsRainMap(_position);
			return current != _active;
		}
	}
}