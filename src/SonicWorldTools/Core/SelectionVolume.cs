using UnityEngine;

namespace SonicWorldTools.Core
{
	/// <summary>Axis-aligned selection bounds (normalized min/max).</summary>
	public struct SelectionVolume
	{
		public Vector3 Min;

		public Vector3 Max;

		public static SelectionVolume FromCorners(Vector3 a, Vector3 b)
		{
			SelectionVolumeMath.Normalize(
				a.x,
				a.y,
				a.z,
				b.x,
				b.y,
				b.z,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			return new SelectionVolume
			{
				Min = new Vector3(minX, minY, minZ),
				Max = new Vector3(maxX, maxY, maxZ),
			};
		}
	}
}
