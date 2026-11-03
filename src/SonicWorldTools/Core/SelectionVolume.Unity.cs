using UnityEngine;

namespace SonicWorldTools.Core
{
	/// <summary>Unity Vector3 overloads for in-game callers (M3+). Not linked into unit tests.</summary>
	public partial struct SelectionVolume
	{
		public static SelectionVolume FromCorners(Vector3 a, Vector3 b)
		{
			return FromCorners(a.x, a.y, a.z, b.x, b.y, b.z);
		}

		public static SelectionVolume FromThreePoints(Vector3 a, Vector3 b, Vector3 c)
		{
			return FromThreePoints(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
		}

		/// <summary>Inclusive on all faces: a point on the boundary is inside.</summary>
		public bool Contains(Vector3 point)
		{
			return Contains(point.x, point.y, point.z);
		}

		public Vector3 Min => new Vector3(MinX, MinY, MinZ);

		public Vector3 Max => new Vector3(MaxX, MaxY, MaxZ);

		public Vector3 Size => new Vector3(SizeX, SizeY, SizeZ);

		public Vector3 Origin => new Vector3(OriginX, OriginY, OriginZ);

		/// <summary>Eight world corners for wire rendering (OBB or AABB).</summary>
		public void GetWorldCorners(Vector3[] corners8)
		{
			if (corners8 == null || corners8.Length < 8)
			{
				return;
			}

			if (IsOriented)
			{
				float cos = Mathf.Cos(Yaw);
				float sin = Mathf.Sin(Yaw);
				corners8[0] = ToWorldCorner(0f, 0f, 0f, cos, sin);
				corners8[1] = ToWorldCorner(ExtX, 0f, 0f, cos, sin);
				corners8[2] = ToWorldCorner(ExtX, 0f, ExtZ, cos, sin);
				corners8[3] = ToWorldCorner(0f, 0f, ExtZ, cos, sin);
				corners8[4] = ToWorldCorner(0f, ExtY, 0f, cos, sin);
				corners8[5] = ToWorldCorner(ExtX, ExtY, 0f, cos, sin);
				corners8[6] = ToWorldCorner(ExtX, ExtY, ExtZ, cos, sin);
				corners8[7] = ToWorldCorner(0f, ExtY, ExtZ, cos, sin);

				return;
			}

			corners8[0] = new Vector3(MinX, MinY, MinZ);
			corners8[1] = new Vector3(MaxX, MinY, MinZ);
			corners8[2] = new Vector3(MaxX, MinY, MaxZ);
			corners8[3] = new Vector3(MinX, MinY, MaxZ);
			corners8[4] = new Vector3(MinX, MaxY, MinZ);
			corners8[5] = new Vector3(MaxX, MaxY, MinZ);
			corners8[6] = new Vector3(MaxX, MaxY, MaxZ);
			corners8[7] = new Vector3(MinX, MaxY, MaxZ);
		}

		private Vector3 ToWorldCorner(float localX, float localY, float localZ, float cos, float sin)
		{
			return new Vector3(
				OriginX + (localX * cos) - (localZ * sin),
				OriginY + localY,
				OriginZ + (localX * sin) + (localZ * cos));
		}
	}
}
