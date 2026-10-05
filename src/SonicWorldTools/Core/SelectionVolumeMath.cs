namespace SonicWorldTools.Core
{
	/// <summary>Axis-aligned bounds math without Unity types (unit-testable).</summary>
	public static class SelectionVolumeMath
	{
		public static void Normalize(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz,
			out float minX,
			out float minY,
			out float minZ,
			out float maxX,
			out float maxY,
			out float maxZ)
		{
			minX = ax < bx ? ax : bx;
			minY = ay < by ? ay : by;
			minZ = az < bz ? az : bz;
			maxX = ax > bx ? ax : bx;
			maxY = ay > by ? ay : by;
			maxZ = az > bz ? az : bz;
		}
	}
}
