using System;

namespace SonicWorldTools.Core
{
	/// <summary>AABB + Y-up oriented box math without Unity types (unit-testable).</summary>
	public static class SelectionVolumeMath
	{
		public const float MinExtent = 1e-4f;

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

		/// <summary>Inclusive on all faces: a point on the boundary is inside.</summary>
		public static bool Contains(
			float minX,
			float minY,
			float minZ,
			float maxX,
			float maxY,
			float maxZ,
			float x,
			float y,
			float z)
		{
			return x >= minX
			       && x <= maxX
			       && y >= minY
			       && y <= maxY
			       && z >= minZ
			       && z <= maxZ;
		}

		public static void Size(
			float minX,
			float minY,
			float minZ,
			float maxX,
			float maxY,
			float maxZ,
			out float sizeX,
			out float sizeY,
			out float sizeZ)
		{
			sizeX = maxX - minX;
			sizeY = maxY - minY;
			sizeZ = maxZ - minZ;
		}

		/// <summary>
		/// Y-up oriented box from three corners: A→B is the width edge (XZ),
		/// C sets depth (perp. to A→B on XZ) and height span (min/max Y of A,B,C).
		/// </summary>
		public static void OrientedFromThreePoints(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz,
			float cx,
			float cy,
			float cz,
			out float originX,
			out float originY,
			out float originZ,
			out float yaw,
			out float extX,
			out float extY,
			out float extZ,
			out float minX,
			out float minY,
			out float minZ,
			out float maxX,
			out float maxY,
			out float maxZ)
		{
			float dx = bx - ax;
			float dz = bz - az;
			extX = (float)Math.Sqrt((dx * dx) + (dz * dz));
			if (extX < MinExtent)
			{
				extX = MinExtent;
				yaw = 0f;
			}
			else
			{
				yaw = (float)Math.Atan2(dz, dx);
			}

			float cos = (float)Math.Cos(yaw);
			float sin = (float)Math.Sin(yaw);
			// local +Z = Cross(local +X, up) = (-sin, 0, cos). Keep A→B as +X always —
			// never flip yaw (that used to put B outside the box when C was on the -Z side).
			float zAxisX = -sin;
			float zAxisZ = cos;
			float relX = cx - ax;
			float relZ = cz - az;
			float localZ = (relX * zAxisX) + (relZ * zAxisZ);

			minY = ay;
			if (by < minY)
			{
				minY = by;
			}

			if (cy < minY)
			{
				minY = cy;
			}

			maxY = ay;
			if (by > maxY)
			{
				maxY = by;
			}

			if (cy > maxY)
			{
				maxY = cy;
			}

			extY = (maxY - minY) < MinExtent ? MinExtent : (maxY - minY);

			// Shift origin along depth so A and C both sit in [0, extZ], regardless of which
			// side of the width edge the player clicked for Corner C.
			if (localZ >= 0f)
			{
				originX = ax;
				originZ = az;
				extZ = localZ < MinExtent ? MinExtent : localZ;
			}
			else
			{
				extZ = (-localZ) < MinExtent ? MinExtent : -localZ;
				originX = ax + (localZ * zAxisX);
				originZ = az + (localZ * zAxisZ);
			}

			originY = minY;

			WorldAabbFromOriented(
				originX,
				originY,
				originZ,
				yaw,
				extX,
				extY,
				extZ,
				out minX,
				out minY,
				out minZ,
				out maxX,
				out maxY,
				out maxZ);
		}

		public static bool ContainsOriented(
			float originX,
			float originY,
			float originZ,
			float yaw,
			float extX,
			float extY,
			float extZ,
			float x,
			float y,
			float z)
		{
			float dx = x - originX;
			float dy = y - originY;
			float dz = z - originZ;
			float cos = (float)Math.Cos(yaw);
			float sin = (float)Math.Sin(yaw);
			float lx = (dx * cos) + (dz * sin);
			float lz = (dx * -sin) + (dz * cos);
			return lx >= 0f
			       && lx <= extX
			       && dy >= 0f
			       && dy <= extY
			       && lz >= 0f
			       && lz <= extZ;
		}

		public static void WorldAabbFromOriented(
			float originX,
			float originY,
			float originZ,
			float yaw,
			float extX,
			float extY,
			float extZ,
			out float minX,
			out float minY,
			out float minZ,
			out float maxX,
			out float maxY,
			out float maxZ)
		{
			float cos = (float)Math.Cos(yaw);
			float sin = (float)Math.Sin(yaw);
			minX = float.PositiveInfinity;
			minY = float.PositiveInfinity;
			minZ = float.PositiveInfinity;
			maxX = float.NegativeInfinity;
			maxY = float.NegativeInfinity;
			maxZ = float.NegativeInfinity;

			for (int ix = 0; ix < 2; ix++)
			{
				for (int iy = 0; iy < 2; iy++)
				{
					for (int iz = 0; iz < 2; iz++)
					{
						float lx = ix == 0 ? 0f : extX;
						float ly = iy == 0 ? 0f : extY;
						float lz = iz == 0 ? 0f : extZ;
						float wx = originX + (lx * cos) + (lz * -sin);
						float wy = originY + ly;
						float wz = originZ + (lx * sin) + (lz * cos);
						if (wx < minX)
						{
							minX = wx;
						}

						if (wy < minY)
						{
							minY = wy;
						}

						if (wz < minZ)
						{
							minZ = wz;
						}

						if (wx > maxX)
						{
							maxX = wx;
						}

						if (wy > maxY)
						{
							maxY = wy;
						}

						if (wz > maxZ)
						{
							maxZ = wz;
						}
					}
				}
			}
		}

		/// <summary>Writes 8 world corners into xyz triples (24 floats), OBB corner order.</summary>
		public static void OrientedWorldCorners(
			float originX,
			float originY,
			float originZ,
			float yaw,
			float extX,
			float extY,
			float extZ,
			float[] xyz24)
		{
			float cos = (float)Math.Cos(yaw);
			float sin = (float)Math.Sin(yaw);
			int i = 0;
			for (int iy = 0; iy < 2; iy++)
			{
				float ly = iy == 0 ? 0f : extY;
				// bottom/top ring: (0,0), (1,0), (1,1), (0,1) in XZ
				AppendCorner(xyz24, ref i, originX, originY, originZ, cos, sin, 0f, ly, 0f);
				AppendCorner(xyz24, ref i, originX, originY, originZ, cos, sin, extX, ly, 0f);
				AppendCorner(xyz24, ref i, originX, originY, originZ, cos, sin, extX, ly, extZ);
				AppendCorner(xyz24, ref i, originX, originY, originZ, cos, sin, 0f, ly, extZ);
			}
		}

		private static void AppendCorner(
			float[] xyz24,
			ref int i,
			float originX,
			float originY,
			float originZ,
			float cos,
			float sin,
			float lx,
			float ly,
			float lz)
		{
			xyz24[i++] = originX + (lx * cos) + (lz * -sin);
			xyz24[i++] = originY + ly;
			xyz24[i++] = originZ + (lx * sin) + (lz * cos);
		}
	}
}
