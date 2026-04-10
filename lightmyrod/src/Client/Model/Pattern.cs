namespace LightMyRod.Client.Model
{
	class Pattern
	{
		readonly int _sideLength;
		public int SideLength => _sideLength;

		readonly int[] _data;
		public int Length => _data.Length;

		public Pattern(int maxOffset)
		{
			_sideLength = maxOffset + 1;
			_data = new int[_sideLength * _sideLength];
		}

		public int this[int i] => _data[i];
		public ref int this[int x, int z] => ref _data[z * _sideLength + x];
	}
}